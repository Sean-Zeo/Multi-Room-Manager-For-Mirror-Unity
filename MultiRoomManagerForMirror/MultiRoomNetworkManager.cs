using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(SceneInterestManagement))]
public class MultiRoomNetworkManager : NetworkManager
{
    public static MultiRoomNetworkManager Instance;
    [Header("Room Player Prefab")]
    public NetworkIdentity roomPlayerPrefab;

    [HideInInspector]
    public List<RoomInfo> rooms = new List<RoomInfo>();
    public class RoomInfo
    {
        public string roomName;
        public string roomData;
        public string sceneName;
        public int currentPlayers;
        public int maxPlayers;
        public Scene scene;
        public List<NetworkConnectionToClient> playerConnections = new List<NetworkConnectionToClient>();
    }

    private readonly Dictionary<NetworkConnectionToClient, RoomInfo> connectionToRoom = new();
    bool creatingRoom = false;
    public List<CreateRoomRequest> createRoomRequestQueue = new List<CreateRoomRequest>();
    public class CreateRoomRequest
    {
        public NetworkConnectionToClient conn;
        public CreateRoomMessage msg;
    }

    public override void Awake()
    {
        if (Instance != null)
            Destroy(Instance.gameObject);

        Instance = this;
        DontDestroyOnLoad(Instance.gameObject);
        base.Awake();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        rooms.Clear();
        NetworkServer.RegisterHandler<RoomListRequestMessage>(OnRoomListRequest);
        NetworkServer.RegisterHandler<CreateRoomMessage>(OnCreateRoom);
        NetworkServer.RegisterHandler<JoinRoomMessage>(OnJoinRoom);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        NetworkServer.UnregisterHandler<RoomListRequestMessage>();
        NetworkServer.UnregisterHandler<CreateRoomMessage>();
        NetworkServer.UnregisterHandler<JoinRoomMessage>();
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnServerDisconnect(conn);
        if (connectionToRoom.ContainsKey(conn) && connectionToRoom[conn] != null)
        {
            RoomInfo info = connectionToRoom[conn];
            info.currentPlayers--;
            info.playerConnections.Remove(conn);

            if (connectionToRoom.ContainsKey(conn))
                connectionToRoom.Remove(conn);

            if (info.currentPlayers <= 0)
            {
                if (info.scene != null)
                    StartCoroutine(UnloadEmptyScene(info.scene));
                rooms.Remove(info);
            }
        }
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        if (NetworkServer.active)
            NetworkServer.Shutdown();
        SceneManager.LoadScene(0,LoadSceneMode.Single);
    }

    public override void Update()
    {
        base.Update();
        if (createRoomRequestQueue.Count > 0)
        {
            if (!creatingRoom)
            {
                if (createRoomRequestQueue[0].conn != null && NetworkServer.connections.ContainsKey(createRoomRequestQueue[0].conn.connectionId))
                    StartCoroutine(CreateRoomCoroutine(createRoomRequestQueue[0].conn, createRoomRequestQueue[0].msg));
                createRoomRequestQueue.RemoveAt(0);
            }
        }
    }

    private void OnRoomListRequest(NetworkConnectionToClient conn, RoomListRequestMessage msg)
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

    private void OnCreateRoom(NetworkConnectionToClient conn, CreateRoomMessage msg)
    {
        if (connectionToRoom.ContainsKey(conn))
        {
            Debug.LogWarning($"[Server] {conn} already in room; create ignored.");
            return;
        }

        if (rooms.Exists(r => r.roomName == msg.roomName))
        {
            Debug.LogWarning($"[Server] Room '{msg.roomName}' already exists; ignoring.");
            return;
        }

        CreateRoomRequest newRequest = new CreateRoomRequest();
        newRequest.conn = conn;
        newRequest.msg = msg;
        createRoomRequestQueue.Add(newRequest);
    }

    private void OnJoinRoom(NetworkConnectionToClient conn, JoinRoomMessage msg)
    {
        if (connectionToRoom.ContainsKey(conn)) return;
        var info = rooms.Find(r => r.roomName == msg.roomName);
        if (info == null || info.currentPlayers >= info.maxPlayers) return;

        if (roomPlayerPrefab != null)
        {
            NetworkIdentity newRoomPlayer = Instantiate(roomPlayerPrefab);
            NetworkServer.ReplacePlayerForConnection(conn, newRoomPlayer.gameObject, ReplacePlayerOptions.Destroy);
        }
        SceneManager.MoveGameObjectToScene(conn.identity.gameObject, info.scene);
        SceneMessage sceneMessage = new SceneMessage();
        sceneMessage.sceneName = info.sceneName;
        sceneMessage.sceneOperation = SceneOperation.LoadAdditive;
        conn.Send(sceneMessage);

        connectionToRoom[conn] = info;
        info.currentPlayers++;
        info.playerConnections.Add(conn);
    }

    IEnumerator CreateRoomCoroutine(NetworkConnectionToClient conn, CreateRoomMessage msg)
    {
        creatingRoom = true;
        LoadSceneParameters parameters = new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.None);
        var loadOp = SceneManager.LoadSceneAsync(msg.sceneName, parameters);
        while (!loadOp.isDone) 
            yield return null;

        Scene newScene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
        RoomInfo info = new RoomInfo
        {
            roomName = msg.roomName,
            roomData = msg.roomData,
            sceneName = msg.sceneName,
            maxPlayers = msg.maxPlayers,
            scene = newScene
        };

        if (conn != null && NetworkServer.connections.ContainsKey(conn.connectionId))
        {
            if (roomPlayerPrefab != null)
            {
                NetworkIdentity newRoomPlayer = Instantiate(roomPlayerPrefab);
                NetworkServer.ReplacePlayerForConnection(conn, newRoomPlayer.gameObject, ReplacePlayerOptions.Destroy);
            }
            SceneManager.MoveGameObjectToScene(conn.identity.gameObject, info.scene);
            SceneMessage sceneMessage = new SceneMessage();
            sceneMessage.sceneName = msg.sceneName;
            sceneMessage.sceneOperation = SceneOperation.LoadAdditive;
            conn.Send(sceneMessage);

            info.currentPlayers++;
            info.playerConnections.Add(conn);
            connectionToRoom[conn] = info;
            rooms.Add(info);
        }
        else
        {
            StartCoroutine(UnloadEmptyScene(newScene));
        }
        creatingRoom = false;
    }

    IEnumerator UnloadEmptyScene(Scene scene)
    {
        yield return SceneManager.UnloadSceneAsync(scene);
    }

}
