using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// A specialized input buffer designed for use with Unity Netcode for GameObjects
/// that handles input synchronization and tick management
/// </summary>
public class NetworkedInputBuffer<T> where T : struct, INetworkSerializable
{
    private Dictionary<uint, T> buffer;
    private int maxBufferSize;
    private NetworkManager networkManager;
    private uint oldestTick;
    
    // Events
    public delegate void BufferTickEvent(uint tick, T input);
    public event BufferTickEvent OnInputAdded;
    public event BufferTickEvent OnInputOverwritten;
    public event Action<uint> OnInputRemoved;
    public event Action OnBufferCleared;
    
    public int Count => buffer.Count;
    public int MaxSize => maxBufferSize;
    public uint OldestTick => oldestTick;
    public uint CurrentTick => (uint)networkManager.NetworkTickSystem.LocalTime.Tick;
    
    /// <summary>
    /// Creates a new networked input buffer
    /// </summary>
    /// <param name="networkManager">Reference to NetworkManager</param>
    /// <param name="bufferSize">Maximum number of inputs to store</param>
    public NetworkedInputBuffer(NetworkManager networkManager, int bufferSize = 120)
    {
        if (bufferSize <= 0)
            throw new ArgumentException("Buffer size must be greater than 0", nameof(bufferSize));
            
        buffer = new Dictionary<uint, T>(bufferSize);
        maxBufferSize = bufferSize;
        oldestTick = 0;
        this.networkManager = networkManager;
        
        // Subscribe to network tick updates
        networkManager.NetworkTickSystem.Tick += OnNetworkTick;
    }
    
    /// <summary>
    /// Handle network tick updates
    /// </summary>
    private void OnNetworkTick()
    {
        // Cleanup old inputs if buffer is getting too large
        EnsureBufferSize();
    }
    
    /// <summary>
    /// Add an input to the buffer for a specific tick
    /// </summary>
    /// <param name="tick">The tick number for this input</param>
    /// <param name="input">The input data to store</param>
    /// <returns>True if a new input was added, false if an existing input was overwritten</returns>
    public bool AddInput(uint tick, T input)
    {
        bool isNewInput = !buffer.ContainsKey(tick);
        
        if (!isNewInput)
        {
            OnInputOverwritten?.Invoke(tick, input);
        }
        
        buffer[tick] = input;
        OnInputAdded?.Invoke(tick, input);
        
        // If we're over capacity, remove oldest inputs
        EnsureBufferSize();
        
        return isNewInput;
    }
    
    /// <summary>
    /// Add an input for the current network tick
    /// </summary>
    /// <param name="input">The input data to store</param>
    /// <returns>The tick number the input was stored at</returns>
    public uint AddInputAtCurrentTick(T input)
    {
        uint currentTick = (uint)networkManager.NetworkTickSystem.LocalTime.Tick;
        AddInput(currentTick, input);
        return currentTick;
    }
    
    /// <summary>
    /// Try to get an input for a specific tick
    /// </summary>
    /// <param name="tick">The tick to retrieve input for</param>
    /// <param name="input">The output input data if found</param>
    /// <returns>True if input was found for the tick, false otherwise</returns>
    public bool TryGetInput(uint tick, out T input)
    {
        return buffer.TryGetValue(tick, out input);
    }
    
    /// <summary>
    /// Get all inputs within a range of ticks
    /// </summary>
    /// <param name="startTick">Starting tick (inclusive)</param>
    /// <param name="endTick">Ending tick (inclusive)</param>
    /// <returns>Dictionary of inputs with tick as key</returns>
    public Dictionary<uint, T> GetInputRange(uint startTick, uint endTick)
    {
        Dictionary<uint, T> result = new Dictionary<uint, T>();
        
        for (uint tick = startTick; tick <= endTick; tick++)
        {
            if (buffer.TryGetValue(tick, out T input))
            {
                result.Add(tick, input);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Get all inputs with ticks less than or equal to the specified tick
    /// </summary>
    /// <param name="tick">Maximum tick (inclusive)</param>
    /// <returns>Dictionary of inputs with tick as key</returns>
    public Dictionary<uint, T> GetInputsUpToTick(uint tick)
    {
        Dictionary<uint, T> result = new Dictionary<uint, T>();
        
        foreach (var pair in buffer)
        {
            if (pair.Key <= tick)
            {
                result.Add(pair.Key, pair.Value);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Get all ticks in the buffer
    /// </summary>
    /// <returns>Array of tick numbers</returns>
    public uint[] GetAllTicks()
    {
        uint[] ticks = new uint[buffer.Count];
        buffer.Keys.CopyTo(ticks, 0);
        return ticks;
    }
    
    /// <summary>
    /// Remove a specific tick from the buffer
    /// </summary>
    /// <param name="tick">The tick to remove</param>
    /// <returns>True if the tick was found and removed, false otherwise</returns>
    public bool RemoveInput(uint tick)
    {
        bool result = buffer.Remove(tick);
        if (result)
        {
            OnInputRemoved?.Invoke(tick);
            UpdateOldestTick();
        }
        return result;
    }
    
    /// <summary>
    /// Remove all inputs before a specific tick
    /// </summary>
    /// <param name="tick">Remove inputs with tick less than this value</param>
    /// <returns>Number of inputs removed</returns>
    public int RemoveInputsBefore(uint tick)
    {
        List<uint> ticksToRemove = new List<uint>();
        
        foreach (uint inputTick in buffer.Keys)
        {
            if (inputTick < tick)
            {
                ticksToRemove.Add(inputTick);
            }
        }
        
        foreach (uint inputTick in ticksToRemove)
        {
            buffer.Remove(inputTick);
            OnInputRemoved?.Invoke(inputTick);
        }
        
        UpdateOldestTick();
        return ticksToRemove.Count;
    }
    
    /// <summary>
    /// Clear all inputs from the buffer
    /// </summary>
    public void Clear()
    {
        buffer.Clear();
        oldestTick = 0;
        OnBufferCleared?.Invoke();
    }
    
    /// <summary>
    /// Get the newest (highest) tick in the buffer
    /// </summary>
    /// <returns>The newest tick, or 0 if buffer is empty</returns>
    public uint GetNewestTick()
    {
        if (buffer.Count == 0)
            return 0;
            
        uint newestTick = 0;
        foreach (uint tick in buffer.Keys)
        {
            if (tick > newestTick)
                newestTick = tick;
        }
        
        return newestTick;
    }
    
    /// <summary>
    /// Change the maximum buffer size
    /// </summary>
    /// <param name="newSize">New maximum size</param>
    public void ResizeBuffer(int newSize)
    {
        if (newSize <= 0)
            throw new ArgumentException("Buffer size must be greater than 0", nameof(newSize));
            
        maxBufferSize = newSize;
        EnsureBufferSize();
    }
    
    /// <summary>
    /// Convert a server tick to a predicted client tick given the current network RTT
    /// </summary>
    /// <param name="serverTick">The server tick to convert</param>
    /// <returns>The equivalent client tick considering network delay</returns>
    public uint ServerTickToClientTick(uint serverTick)
    {
        // Calculate RTT in ticks (approximately half RTT for one-way journey)
        // We use RTT / 2 as an approximation of one-way latency
        float rttInTicks = (networkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(0) / 1000f) * networkManager.NetworkConfig.TickRate / 2f;
        
        // Add the tick offset to compensate for latency
        return serverTick + (uint)Mathf.Ceil(rttInTicks);
    }
    
    /// <summary>
    /// Convert a client tick to a predicted server tick given the current network RTT
    /// </summary>
    /// <param name="clientTick">The client tick to convert</param>
    /// <returns>The equivalent server tick considering network delay</returns>
    public uint ClientTickToServerTick(uint clientTick)
    {
        // Calculate RTT in ticks (approximately half RTT for one-way journey)
        float rttInTicks = (networkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(0) / 1000f) * networkManager.NetworkConfig.TickRate / 2f;
        
        // Subtract the tick offset to compensate for latency
        return clientTick > (uint)Mathf.Ceil(rttInTicks) ? clientTick - (uint)Mathf.Ceil(rttInTicks) : 0;
    }
    
    /// <summary>
    /// Ensures the buffer doesn't exceed the maximum size by removing oldest entries
    /// </summary>
    private void EnsureBufferSize()
    {
        if (buffer.Count <= maxBufferSize)
            return;
            
        int itemsToRemove = buffer.Count - maxBufferSize;
        List<uint> sortedTicks = new List<uint>(buffer.Keys);
        sortedTicks.Sort();
        
        for (int i = 0; i < itemsToRemove; i++)
        {
            uint tick = sortedTicks[i];
            buffer.Remove(tick);
            OnInputRemoved?.Invoke(tick);
        }
        
        UpdateOldestTick();
    }
    
    /// <summary>
    /// Updates the oldest tick tracking
    /// </summary>
    private void UpdateOldestTick()
    {
        if (buffer.Count == 0)
        {
            oldestTick = 0;
            return;
        }
        
        oldestTick = uint.MaxValue;
        foreach (uint tick in buffer.Keys)
        {
            if (tick < oldestTick)
                oldestTick = tick;
        }
    }
    
    /// <summary>
    /// Clean up resources when object is destroyed
    /// </summary>
    public void Dispose()
    {
        if (networkManager != null)
        {
            networkManager.NetworkTickSystem.Tick -= OnNetworkTick;
        }
    }
}