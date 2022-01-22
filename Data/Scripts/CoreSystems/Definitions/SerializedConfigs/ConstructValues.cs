using CoreSystems.Support;
using ProtoBuf;
using static CoreSystems.Support.Ai;
namespace CoreSystems
{
    [ProtoContract]
    public class ConstructDataValues
    {
        [ProtoMember(1)] public int Version = Session.VersionControl;
        [ProtoMember(2)] public FocusData FocusData;

        public bool Sync(Constructs construct, ConstructDataValues sync, bool localCall = false)
        {
            FocusData.Sync(construct.RootAi, sync.FocusData, localCall);
            return true;
        }
    }

    [ProtoContract]
    public class FocusData
    {
        public enum LockModes
        {
            None,
            Locked,
        }

        [ProtoMember(1)] public uint Revision;
        [ProtoMember(2)] public long[] Target;
        [ProtoMember(3)] public int ActiveId;
        [ProtoMember(4)] public bool HasFocus;
        [ProtoMember(5)] public float DistToNearestFocusSqr;
        [ProtoMember(6)] public LockModes[] Locked;


        public bool Sync(Ai ai, FocusData sync, bool localCall = false)
        {
            if (ai.Session.IsServer || sync.Revision > Revision)
            {
                Revision = sync.Revision;
                ActiveId = sync.ActiveId;
                HasFocus = sync.HasFocus;
                DistToNearestFocusSqr = sync.DistToNearestFocusSqr;

                for (int i = 0; i < Target.Length; i++) {
                    Target[i] = sync.Target[i];
                    Locked[i] = sync.Locked[i];
                }

                if (ai == ai.Construct.RootAi && localCall)
                    ai.Construct.UpdateLeafFoci();

                return true;
            }
            return false;
        }
    }
}
