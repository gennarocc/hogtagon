using Unity.Netcode;

public struct ClientInput : INetworkSerializable
{
    public ulong clientId;
    public float throttleInput;
    public float brakeInput;
    public bool handbrake;
    public float steerInput;
    public bool jumpInput;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref throttleInput);
        serializer.SerializeValue(ref brakeInput);
        serializer.SerializeValue(ref handbrake);
        serializer.SerializeValue(ref steerInput);
        serializer.SerializeValue(ref jumpInput);
    }
}