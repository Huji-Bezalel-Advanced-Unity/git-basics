// ════════════════════════════════════════════════════════════════════
//   GameManager.cs  ──  Game Manager (Singleton)
// ════════════════════════════════════════════════════════════════════

using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("── Player Reference ──")]
    [Tooltip("Drag the Player object here from the Inspector")]
    public PlayerController player;

    void Awake()
    {
        // Implement Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);  // Persist across scenes
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Example: Calculate the player's distance from the starting point (Vector3.zero)
    public float GetDistanceFromOrigin()
    {
        if (player == null) return 0f;
        return Vector3.Distance(Vector3.zero, player.transform.position);
    }
}