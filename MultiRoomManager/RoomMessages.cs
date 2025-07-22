using Mirror;

public struct RoomListRequestMessage : NetworkMessage { }

public struct RoomListResponseMessage : NetworkMessage
{
    public string[] roomNames;
    public string[] roomDatas;
    public string[] sceneNames;
    public int[] currentCounts;
    public int[] maxCounts;
}

public struct CreateRoomMessage : NetworkMessage
{
    public string roomName;
    public string roomData;
    public string sceneName;
    public int maxPlayers;
}

public struct JoinRoomMessage : NetworkMessage
{
    public string roomName;
}
