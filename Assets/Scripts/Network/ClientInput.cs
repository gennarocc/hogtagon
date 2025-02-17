using Unity.Netcode;

public struct ClientInput : INetworkSerializable
{
    public ulong clientId;
    public float moveInput;
    public float brakeInput;
    public float steeringAngle;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref moveInput);
        serializer.SerializeValue(ref brakeInput);
        serializer.SerializeValue(ref steeringAngle);
    }
}