using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

//Replace NetworkManager with this, make sure "Don't destroy on load" is set to false, and make sure the lobby player is the original player prefab
//You must also ensure "autospawn" is enabled for the player
//When the player enters a room, it will change player prefab to the room player prefab
[RequireComponent(typeof(SceneInterestManagement))]
public class MultiRoomNetworkManager : NetworkManager
{

    public class RoomInfo
    {
        public string roomName;
        public string roomData;
        public string sceneName;
        public int currentPlayers;
        public int maxPlayers;
        public Scene scene;
        //List of connections in this room, you can extend this for player management on server side, such as kicking all players from a room
        public List<NetworkConnectionToClient> playerConnections = new List<NetworkConnectionToClient>();
    }

    [Header("Physics")]
    public LocalPhysicsMode roomPhysicsMode = LocalPhysicsMode.None; //Physics choice for each room

    [Header("Prefabs")]
    public GameObject lobbyPlayerPrefab;  //Your lobby‑only player prefab
    public GameObject roomPlayerPrefab;   //Your in‑room player prefab

    [HideInInspector]
    public List<RoomInfo> rooms = new List<RoomInfo>();

    //Map a client connection to the room they're in, this is a very performant way to prevent room creation/joining exploits
    readonly Dictionary<NetworkConnectionToClient, RoomInfo> connectionToRoom = new();

    public override void OnStartServer()
    {
        base.OnStartServer();
        rooms.Clear();

        NetworkServer.RegisterHandler<RoomListRequestMessage>(OnRoomListRequest);
        NetworkServer.RegisterHandler<CreateRoomMessage>(OnCreateRoom);
        NetworkServer.RegisterHandler<JoinRoomMessage>(OnJoinRoom);
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        //Spawn them into the lobby
        GameObject lobbyGO = Instantiate(lobbyPlayerPrefab);
        NetworkServer.AddPlayerForConnection(conn, lobbyGO);
    }

    //SERVER: clean up empty rooms when last player disconnects
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        //Identify which scene the disconnecting player was in
        var oldIdentity = conn.identity;
        if (oldIdentity != null)
        {
            Scene playerScene = oldIdentity.gameObject.scene;
            //Find the matching RoomInfo
            RoomInfo info = rooms.Find(r => r.scene == playerScene);
            if (info != null)
            {
                //Decrement count and remove connection
                info.currentPlayers--;
                info.playerConnections.Remove(conn);
                //Cleanup connectionToRoom
                if (connectionToRoom.ContainsKey(conn))
                    connectionToRoom.Remove(conn);
                //if that was last player, unload server‑side
                if (info.currentPlayers <= 0)
                    StartCoroutine(UnloadRoomWhenEmpty(info));
            }
        }

        //Continue Mirror’s normal disconnect cleanup
        base.OnServerDisconnect(conn);
    }

    IEnumerator UnloadRoomWhenEmpty(RoomInfo info)
    {
        //Unload the additive scene
        yield return SceneManager.UnloadSceneAsync(info.scene);
        //remove from list so lobby no longer shows it
        rooms.Remove(info);
    }

    //SERVER: handle lobby‑client messages
    void OnRoomListRequest(NetworkConnectionToClient conn, RoomListRequestMessage msg)
    {
        int n = rooms.Count;
        var resp = new RoomListResponseMessage
        {
            roomNames = new string[n],
            roomDatas = new string[n],
            sceneNames = new string[n],
            currentCounts = new int[n],
            maxCounts = new int[n]
        };

        for (int i = 0; i < n; i++)
        {
            var r = rooms[i];
            resp.roomNames[i] = r.roomName;
            resp.roomDatas[i] = r.roomData;
            resp.sceneNames[i] = r.sceneName;
            resp.currentCounts[i] = r.currentPlayers;
            resp.maxCounts[i] = r.maxPlayers;
        }

        conn.Send(resp);
    }

    void OnCreateRoom(NetworkConnectionToClient conn, CreateRoomMessage msg)
    {
        //Check if player is already in a room, if so, forbid the creation of a new room until they leave the existing room
        if (connectionToRoom.ContainsKey(conn))
        {
            Debug.LogWarning($"[Server] {conn} already in room '{connectionToRoom[conn].roomName}'; create ignored.");
            return;
        }

        //Prevent duplicate room names, this is optional and can be removed if you like
        if (rooms.Exists(r => r.roomName == msg.roomName))
        {
            Debug.LogWarning($"[Server] Room '{msg.roomName}' already exists; ignoring.");
            return;
        }
        StartCoroutine(CreateRoomCoroutine(conn, msg));
    }

    IEnumerator CreateRoomCoroutine(NetworkConnectionToClient conn, CreateRoomMessage msg)
    {
        //1) Load scene additively on server (isolated physics based on choice within inspector)
        var loadOp = SceneManager.LoadSceneAsync(
            msg.sceneName,
            new LoadSceneParameters
            {
                loadSceneMode = LoadSceneMode.Additive,
                localPhysicsMode = roomPhysicsMode
            });
        yield return loadOp;

        Scene newScene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);

        //2) Register room
        var info = new RoomInfo
        {
            roomName = msg.roomName,
            roomData = msg.roomData,
            sceneName = msg.sceneName,
            currentPlayers = 0,
            maxPlayers = msg.maxPlayers,
            scene = newScene
        };
        rooms.Add(info);

        //3) Tell client to load the scene
        conn.Send(new SceneMessage
        {
            sceneName = msg.sceneName,
            sceneOperation = SceneOperation.LoadAdditive
        });
        yield return null; //wait one frame

        //4) Swap their lobby player for a room player
        var roomGO = Instantiate(roomPlayerPrefab);
        NetworkServer.ReplacePlayerForConnection(conn, roomGO, true);

        //5) Move the new player into the room scene
        SceneManager.MoveGameObjectToScene(conn.identity.gameObject, newScene);
        info.currentPlayers++;
        info.playerConnections.Add(conn);
        connectionToRoom[conn] = info;
    }

    void OnJoinRoom(NetworkConnectionToClient conn, JoinRoomMessage msg)
    {
        //Check if player is already in a room, if so, forbid joining an additional room
        if (connectionToRoom.ContainsKey(conn))
            return; 

        var info = rooms.Find(r => r.roomName == msg.roomName);
        if (info == null || info.currentPlayers >= info.maxPlayers)
            return;
        StartCoroutine(JoinRoomCoroutine(conn, info));
    }

    IEnumerator JoinRoomCoroutine(NetworkConnectionToClient conn, RoomInfo info)
    {
        //1) Tell client to load the scene
        conn.Send(new SceneMessage
        {
            sceneName = info.sceneName,
            sceneOperation = SceneOperation.LoadAdditive
        });
        yield return null;

        //2) Swap in their room‑player
        var roomGO = Instantiate(roomPlayerPrefab);
        NetworkServer.ReplacePlayerForConnection(conn, roomGO, true);

        //3) Move them into the room scene
        SceneManager.MoveGameObjectToScene(conn.identity.gameObject, info.scene);
        info.currentPlayers++;
        info.playerConnections.Add(conn);
        connectionToRoom[conn] = info;
    }

    //CLIENT: track lobby scene & unload rooms on disconnect
    public override void OnStopClient()
    {
        base.OnStopClient();

        //This is a simple hacky solution to refresh the game if you disconnect
        Application.LoadLevel(0);
    }

    //This only works on the server, useful for getting room information
    //Example: MultiRoomNetworkManager.Instance.GetRoomInfoFromScene(gameObject.scene)
    public RoomInfo GetRoomInfoFromScene(Scene scene)
    {
        //Search through the rooms and select the room with the exact scene
        return rooms.Find(r => r.scene.handle == scene.handle);
    }

}
