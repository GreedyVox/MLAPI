using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.NetCode.Data
{
    public interface IPayload
    {
        public int NetworkID { get; set; }
        /// <summary>
        /// The object has been spawned, write the payload data.
        /// </summary>
        bool PayLoad(out FastBufferWriter writer);
        /// <summary>
        /// The object has been spawned, read the payload data.
        /// </summary>
        void PayLoad(in FastBufferReader reader, GameObject go = default);
    }
}