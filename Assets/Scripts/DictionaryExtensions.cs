using System.Collections.Generic;

public static class DictionaryExtensions
{
    public static ClientDataListSerialized ConvertDictionaryToSerializableList(Dictionary<ulong, PlayerData> data)
    {
        ClientDataListSerialized list = new ClientDataListSerialized();
        foreach (var kvp in data)
        {
            list.ClientDataList.Add(new ClientData
            {
                clientId = kvp.Key,
                username = kvp.Value.username,
                score = kvp.Value.score,
                color = kvp.Value.color,
                state = kvp.Value.state,
                isLobbyLeader = kvp.Value.isLobbyLeader
            });
        }
        return list;
    }

    public static Dictionary<ulong, PlayerData> ConvertSerializableListToDictionary(ClientDataListSerialized list)
    {
        Dictionary<ulong, PlayerData> data = new Dictionary<ulong, PlayerData>();
        foreach (var item in list.ClientDataList)
        {
            data[item.clientId] = new PlayerData()
            {
                username = item.username,
                score = item.score,
                color = item.color,
                state = item.state,
                isLobbyLeader = item.isLobbyLeader
            };
        }
        return data;
    }
}