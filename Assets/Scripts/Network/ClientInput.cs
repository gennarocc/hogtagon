using Unity.Netcode;

public struct ClientInput : INetworkSerializable
{
    public ulong clientId;
    public uint tick;
    public float moveInput;
    public float brakeInput;
    public float steerInput;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref tick);
        serializer.SerializeValue(ref moveInput);
        serializer.SerializeValue(ref brakeInput);
        serializer.SerializeValue(ref steerInput);
    }
}