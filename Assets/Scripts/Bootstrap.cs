using UnityEngine;

/// <summary>
/// Wires up every system in the scene. Attach this to a single empty GameObject
/// in an otherwise empty scene and press Play -- everything else is created at runtime.
///
/// IMPORTANT: The NfcManager is created on its own child GameObject literally named
/// "NfcManager", because the Java plugin (NfcUnityActivity.java) calls
///   UnityPlayer.UnitySendMessage("NfcManager", "OnNfcTagDiscovered", uid)
/// and that lookup is by GameObject name.
/// </summary>
public class Bootstrap : MonoBehaviour
{
    void Awake()
    {
        // The order matters: UIManager and GameManager read references to the others in Start().
        var nfcGo = new GameObject("NfcManager");
        nfcGo.transform.SetParent(transform);
        nfcGo.AddComponent<NfcManager>();

        gameObject.AddComponent<TiltDetector>();
        gameObject.AddComponent<NetworkController>();
        gameObject.AddComponent<GameManager>();
        gameObject.AddComponent<UIManager>();
    }
}
