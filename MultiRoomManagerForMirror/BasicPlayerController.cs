using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
public class BasicPlayerController : NetworkBehaviour
{
    public CharacterController controller;
    public NetworkIdentity spawnablePrefab;
    private Vector3 playerVelocity;
    private bool groundedPlayer;
    private float playerSpeed = 2.0f;
    private float jumpHeight = 1.0f;
    private float gravityValue = -9.81f;

    bool isReady = false;
    public override void OnStartClient()
    {
        base.OnStartClient();
        controller.enabled = isOwned;
        isReady = true;
    }

    void Update()
    {
        if (!isReady || !isOwned) 
            return;

        groundedPlayer = controller.isGrounded;
        if (groundedPlayer && playerVelocity.y < 0)
        {
            playerVelocity.y = 0f;
        }

        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        move = Vector3.ClampMagnitude(move, 1f);
        if (move != Vector3.zero)
        {
            transform.forward = move;
        }

        if (Input.GetButtonDown("Jump") && groundedPlayer)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravityValue);
        }

        playerVelocity.y += gravityValue * Time.deltaTime;
        Vector3 finalMove = (move * playerSpeed) + (playerVelocity.y * Vector3.up);
        controller.Move(finalMove * Time.deltaTime);

        if (Input.GetMouseButtonDown(0))
            SpawnNetworkObjectExample(new Vector3(0,5,0));
    }

    [Command]
    void SpawnNetworkObjectExample(Vector3 position, NetworkConnectionToClient sender = null)
    {
        GameObject obj = Instantiate(spawnablePrefab, position, Quaternion.identity).gameObject;
        SceneManager.MoveGameObjectToScene(obj, gameObject.scene);
        NetworkServer.Spawn(obj, sender);
    }
}
