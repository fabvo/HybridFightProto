using UnityEngine;

/// <summary>
/// Wires up every system in the scene. Attach this to a single empty GameObject
/// in an otherwise empty scene and press Play -- everything else is created at runtime.
///
/// Both NfcManager and BluetoothManager need their own GameObjects with those
/// exact names because the Java plugins call
///   UnityPlayer.UnitySendMessage("NfcManager", ...)
///   UnityPlayer.UnitySendMessage("BluetoothManager", ...)
/// and that lookup is by GameObject name.
/// </summary>
public class Bootstrap : MonoBehaviour
{
    void Awake()
    {
        var nfcGo = new GameObject("NfcManager");
        nfcGo.transform.SetParent(transform);
        nfcGo.AddComponent<NfcManager>();

        var btGo = new GameObject("BluetoothManager");
        btGo.transform.SetParent(transform);
        btGo.AddComponent<BluetoothManager>();

        gameObject.AddComponent<TiltDetector>();
        gameObject.AddComponent<NetworkController>();
        gameObject.AddComponent<GameManager>();
        gameObject.AddComponent<UIManager>();
    }
}
