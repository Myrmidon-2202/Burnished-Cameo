#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. EDIT: Modified Version, 2026
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.CA.Traits
{
	public class AirstrikePowerSquadMember
	{
		public readonly string UnitType;
		public readonly WVec SpawnOffset;
		public readonly WVec TargetOffset;
		public readonly int SpawnDelay;


		public AirstrikePowerSquadMember(MiniYamlNode yamlNode)
		{
			UnitType = yamlNode.Key;
			FieldLoader.Load(this, yamlNode.Value);
		}
	}

	public class ClassicAirstrikePowerInfo : SupportPowerInfo
	{
		[FieldLoader.LoadUsing("LoadSquad")]
		[Desc("A list of aircraft in the squad. Each has configurable UnitType, SpawnOffset, TargetOffset and SpawnDelay.")]
		public readonly List<AirstrikePowerSquadMember> Squad;

		public readonly int QuantizedFacings = 32;
		public readonly WDist Cordon = new WDist(5120);

		[ActorReference]
		[Desc("Actor to spawn when the aircraft start attacking")]
		public readonly string CameraActor = null;

		[Desc("Amount of time to keep the camera alive after the aircraft have finished attacking")]
		public readonly int CameraRemoveDelay = 25;

		[Desc("How many attack runs to perform.")]
		public readonly int Strikes = 1;

		[Desc("How long to allow idling in the circle phase between strikes.")]
		public readonly int CircleDelay = 0;

		static object LoadSquad(MiniYaml yaml)
		{
			var ret = new List<AirstrikePowerSquadMember>();
			var squadNode = yaml.Nodes.FirstOrDefault(n => n.Key == "Squad");

			if (squadNode != null)
				foreach (var d in squadNode.Value.Nodes)
					ret.Add(new AirstrikePowerSquadMember(d));

			return ret;
		}

		public override object Create(ActorInitializer init)
		{
			return new ClassicAirstrikePower(init.Self, this);
		}
	}

	public class ClassicAirstrikePower : SupportPower
	{
		readonly ClassicAirstrikePowerInfo info;

		public ClassicAirstrikePower(Actor self, ClassicAirstrikePowerInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		public override void Activate(
			Actor self,
			Order order,
			SupportPowerManager manager)
		{
			base.Activate(self, order, manager);

			var target = order.Target.CenterPosition;

			SendAirstrike(
				self,
				target);
		}

		public void SendAirstrike(Actor self, WPos target)
		{
			var attackFacing =
				256 * self.World.SharedRandom.Next(info.QuantizedFacings)
				/ info.QuantizedFacings;

			Action<Actor> onEnterRange = a =>
			{
				// Camera handling is intentionally omitted here.
				// The old implementation depended on APIs that changed
				// between OpenRA versions.
			};

			Action<Actor> onExitRange = a =>
			{
			};

			Action<Actor> onRemovedFromWorld = a =>
			{
			};

			self.World.AddFrameEndTask(w =>
			{
				var attackRotation = WRot.FromFacing(attackFacing);

				foreach (var squadMember in info.Squad)
				{
					var altitude =
						self.World.Map.Rules.Actors[squadMember.UnitType]
							.TraitInfo<AircraftInfo>()
							.CruiseAltitude.Length;

					var delta =
						new WVec(0, -1024, 0)
							.Rotate(attackRotation);

					var targetPos =
						target + new WVec(0, 0, altitude);

					var startEdge =
						targetPos -
						(self.World.Map.DistanceToEdge(
							targetPos,
							-delta) + info.Cordon).Length *
						delta / 1024;

					var finishEdge =
						targetPos +
						(self.World.Map.DistanceToEdge(
							targetPos,
							delta) + info.Cordon).Length *
						delta / 1024;

					PlayLaunchSounds();

					var spawnOffset =
						squadMember.SpawnOffset
							.Rotate(attackRotation);

					var targetOffset =
						squadMember.TargetOffset
							.Rotate(attackRotation);

					var aircraft = w.CreateActor(
						squadMember.UnitType,
						new TypeDictionary
						{
							new CenterPositionInit(
								startEdge + spawnOffset),

							new OwnerInit(self.Owner),

							new FacingInit(
								WAngle.FromFacing(attackFacing)),

							new CreationActivityDelayInit(
								squadMember.SpawnDelay)
						});

					var attack = aircraft.Trait<AttackBomber>();

					attack.SetTarget(
						targetPos + targetOffset);

					attack.OnEnteredAttackRange +=
						onEnterRange;

					attack.OnExitedAttackRange +=
						onExitRange;

					attack.OnRemovedFromWorld +=
						onRemovedFromWorld;

					for (var strike = 0;
						strike < info.Strikes;
						strike++)
					{
						aircraft.QueueActivity(
							new Fly(
								aircraft,
								Target.FromPos(
									target + spawnOffset)));

						if (info.Strikes > 1)
							aircraft.QueueActivity(
								new Wait(
									info.CircleDelay,
									true));
					}

					aircraft.QueueActivity(
						new Fly(
							aircraft,
							Target.FromPos(
								finishEdge + spawnOffset)));

					aircraft.QueueActivity(
						new RemoveSelf());
				}
			});
		}
	}
}