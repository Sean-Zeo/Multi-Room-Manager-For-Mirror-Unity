using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.SceneManagement;

public class BasicPlayerController : NetworkBehaviour
{
    public Transform spawnablePrefab;
    public CharacterController controller;
    private Vector3 playerVelocity;
    private bool groundedPlayer;
    private float playerSpeed = 2.0f;
    private float jumpHeight = 1.0f;
    private float gravityValue = -9.81f;

    private void Start()
    {
        controller.enabled = isOwned;
    }

    void Update()
    {
        //Disable controls for other players
        if (!isOwned) 
            return;

        groundedPlayer = controller.isGrounded;
        if (groundedPlayer && playerVelocity.y < 0)
        {
            playerVelocity.y = 0f;
        }

        // Horizontal input
        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        move = Vector3.ClampMagnitude(move, 1f); // Optional: prevents faster diagonal movement

        if (move != Vector3.zero)
        {
            transform.forward = move;
        }

        // Jump
        if (Input.GetButtonDown("Jump") && groundedPlayer)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravityValue);
        }

        // Apply gravity
        playerVelocity.y += gravityValue * Time.deltaTime;

        // Combine horizontal and vertical movement
        Vector3 finalMove = (move * playerSpeed) + (playerVelocity.y * Vector3.up);
        controller.Move(finalMove * Time.deltaTime);

        if (Input.GetMouseButtonDown(0))
        {
            // Example of spawning a network object within the room
            Vector3 pos = new Vector3(0, 5, 0);
            SpawnNetworkObjectExample(pos);
        }
    }

    [Command]
    void SpawnNetworkObjectExample(Vector3 position, NetworkConnectionToClient sender = null)
    {
        // Instantiate on server
        GameObject obj = Instantiate(spawnablePrefab, position, Quaternion.identity).gameObject;
        // Move into this player’s scene
        Scene playerScene = gameObject.scene;
        SceneManager.MoveGameObjectToScene(obj, playerScene);
        // Spawn for everyone in the room and also give ownership to the player
        NetworkServer.Spawn(obj, sender);
    }
}
