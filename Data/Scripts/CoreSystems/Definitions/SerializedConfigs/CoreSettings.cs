using ProtoBuf;
using VRage.Input;
using VRageMath;
using WeaponCore.Data.Scripts.CoreSystems.Support;

namespace CoreSystems.Settings
{
    public class CoreSettings
    {
        internal readonly VersionControl VersionControl;
        internal ServerSettings Enforcement;
        internal ClientSettings ClientConfig;
        internal Session Session;
        internal bool ClientWaiting;
        internal CoreSettings(Session session)
        {
            Session = session;
            VersionControl = new VersionControl(this);
            VersionControl.InitSettings();
            if (Session.IsClient)
                ClientWaiting = true;
        }

        [ProtoContract]
        public class ServerSettings
        {
            [ProtoContract]
            public class BlockModifer
            {
                [ProtoMember(1)] public string SubTypeId;
                [ProtoMember(2)] public float DirectDamageModifer;
                [ProtoMember(3)] public float AreaDamageModifer;
            }

            [ProtoContract]
            public class ShipSize
            {
                [ProtoMember(1)] public string Name;
                [ProtoMember(2)] public int BlockCount;
                [ProtoMember(3)] public bool LargeGrid;
            }

            [ProtoContract]
            public class Modifiers
            {
                [ProtoMember(1)] public AmmoMod[] Ammos;
                [ProtoMember(2)] public WeaponMod[] Weapons;
            }

            [ProtoContract]
            public struct AmmoMod
            {
                [ProtoMember(1)] public string AmmoName;
                [ProtoMember(2)] public string Variable;
                [ProtoMember(3)] public string Value;
            }

            [ProtoContract]
            public struct WeaponMod
            {
                [ProtoMember(1)] public string PartName;
                [ProtoMember(2)] public string Variable;
                [ProtoMember(3)] public string Value;
            }

            [ProtoMember(1)] public int Version = -1;
            [ProtoMember(2)] public int Debug = -1;
            [ProtoMember(3)] public bool AdvancedOptimizations = false;
            [ProtoMember(4)] public float DirectDamageModifer = 1;
            [ProtoMember(5)] public float AreaDamageModifer = 1;
            [ProtoMember(6)] public float ShieldDamageModifer = 1;
            [ProtoMember(7)] public bool BaseOptimizations = true;
            [ProtoMember(8)] public bool ServerSleepSupport = false;
            [ProtoMember(9)] public bool DisableAi;
            [ProtoMember(10)] public bool DisableLeads;
            [ProtoMember(11)] public double MinHudFocusDistance;
            [ProtoMember(12)] public double MaxHudFocusDistance = 10000;
            [ProtoMember(13)]
            public BlockModifer[] BlockModifers =
            {
                new BlockModifer {SubTypeId = "TestSubId1", DirectDamageModifer = 0.5f, AreaDamageModifer = 0.1f},
                new BlockModifer {SubTypeId = "TestSubId2", DirectDamageModifer = -1f, AreaDamageModifer = 0f }
            };
            [ProtoMember(14)]
            public ShipSize[] ShipSizes =
            {
                new ShipSize {Name = "Scout", BlockCount = 0, LargeGrid = false },
                new ShipSize {Name = "Fighter", BlockCount = 2000, LargeGrid = false },
                new ShipSize {Name = "Frigate", BlockCount = 0, LargeGrid = true },
                new ShipSize {Name = "Destroyer", BlockCount = 3000, LargeGrid = true },
                new ShipSize {Name = "Cruiser", BlockCount = 6000, LargeGrid = true },
                new ShipSize {Name = "Battleship", BlockCount = 12000, LargeGrid = true },
                new ShipSize {Name = "Capital", BlockCount = 24000, LargeGrid = true },
            };
            [ProtoMember(15)]
            public Modifiers ServerModifiers = new Modifiers
            {
                Ammos = new[] { 
                    new AmmoMod { AmmoName = "AmmoRound1", Variable = "BaseDamage", Value = "1" }, 
                    new AmmoMod { AmmoName = "AmmoRound1", Variable = "EnergyAreaEffectDamage", Value = "false" }, 
                    new AmmoMod { AmmoName = "AmmoRound2", Variable = "DesiredSpeed", Value = "750" } 
                },
                Weapons = new[]
                {
                    new WeaponMod {PartName = "PartName1", Variable = "MaxTargetDistance", Value = "1500"},
                    new WeaponMod {PartName = "PartName2", Variable = "DeviateShotAngle", Value = "0.25"},
                    new WeaponMod {PartName = "PartName2", Variable = "AimingTolerance", Value = "0.1"},
                },
            };
        }

        [ProtoContract]
        public class ClientSettings
        {
            [ProtoMember(1)] public int Version = -1;
            [ProtoMember(2)] public bool ClientOptimizations = true;
            [ProtoMember(3)] public int MaxProjectiles = 15000;
            [ProtoMember(4)] public string MenuButton = MyMouseButtonsEnum.Middle.ToString();
            [ProtoMember(5)] public string ControlKey = MyKeys.R.ToString();
            [ProtoMember(6)] public bool ShowHudTargetSizes; // retired
            [ProtoMember(7)] public string ActionKey = MyKeys.NumPad0.ToString();
            [ProtoMember(8)] public Vector2 HudPos = new Vector2(0, 0);
            [ProtoMember(9)] public float HudScale = 1f;
            [ProtoMember(10)] public string InfoKey = MyKeys.Decimal.ToString();
            [ProtoMember(11)] public bool MinimalHud = false;
            [ProtoMember(12)] public bool StikcyPainter = false;
        }
    }
}
