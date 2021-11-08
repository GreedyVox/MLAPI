using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.Networked.Data {
    public struct PayloadItemPickup : INetworkSerializable {
        public long OwnerID;
        public int ItemCount;
        public Vector3 Torque;
        public Vector3 Velocity;
        public uint[] ItemID {
            set { ItemID = value; }
            get { return ItemID ?? new uint[0]; }
        }
        public int[] ItemAmounts {
            set { ItemAmounts = value; }
            get { return ItemAmounts ?? new int[0]; }
        }
        public void NetworkSerialize<T> (BufferSerializer<T> serializer) where T : IReaderWriter {
            serializer.SerializeValue (ref ItemCount);
            if (serializer.IsReader) {
                ItemID = new uint[ItemCount];
                ItemAmounts = new int[ItemCount];
            }
            for (int n = 0; n < ItemCount; n++) {
                serializer.SerializeValue (ref ItemID[n]);
                serializer.SerializeValue (ref ItemAmounts[n]);
            }
            serializer.SerializeValue (ref OwnerID);
            serializer.SerializeValue (ref Velocity);
            serializer.SerializeValue (ref Torque);
        }
    }
}