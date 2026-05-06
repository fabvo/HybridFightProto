using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// C# façade in front of NearbyConnectionsBridge.java.
///
/// MUST live on a GameObject named "NearbyManager" because the Java side calls
/// UnityPlayer.UnitySendMessage("NearbyManager", ...).
///
/// Responsibilities:
///   1. Requests the runtime permissions that Nearby Connections needs.
///      The API split changed twice (Android 12 and Android 13), we handle both.
///   2. Forwards Unity calls (StartFindingPeer, Stop, Send) to the Java bridge.
///   3. Re-broadcasts Java events as C# events the rest of the game subscribes to.
/// </summary>
public class NearbyConnectionsManager : MonoBehaviour
{
    public static NearbyConnectionsManager Instance { get; private set; }

    public bool   IsConnected  { get; private set; }
    public string Status       { get; private set; } = "Bereit";
    public string LastError    { get; private set; }

    public event System.Action          OnConnected;
    public event System.Action          OnDisconnected;
    public event System.Action<string>  OnMessage;
    public event System.Action<string>  OnStatusChanged;
    public event System.Action<string>  OnError;

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaClass bridge;
#endif

    void Awake()
    {
        Instance = this;
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge = new AndroidJavaClass("com.example.hybridfight.NearbyConnectionsBridge");
#endif
    }

    void Start()
    {
        // Kick off a single batched permission request at app start so the user
        // sees the prompts up-front rather than the moment they tap Find Peer.
        EnsurePermissions();
    }

    public void EnsurePermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        var needed = NeededPermissions();
        var toRequest = new List<string>();
        foreach (var p in needed)
            if (!Permission.HasUserAuthorizedPermission(p))
                toRequest.Add(p);
        if (toRequest.Count > 0)
            Permission.RequestUserPermissions(toRequest.ToArray());
#endif
    }

    public bool HasAllPermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        foreach (var p in NeededPermissions())
            if (!Permission.HasUserAuthorizedPermission(p)) return false;
        return true;
#else
        return false;
#endif
    }

    List<string> NeededPermissions()
    {
        var list = new List<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
        int sdk = GetSdkInt();
        if (sdk >= 31)
        {
            list.Add("android.permission.BLUETOOTH_ADVERTISE");
            list.Add("android.permission.BLUETOOTH_CONNECT");
            list.Add("android.permission.BLUETOOTH_SCAN");
        }
        if (sdk >= 33)
            list.Add("android.permission.NEARBY_WIFI_DEVICES");
        else
            list.Add("android.permission.ACCESS_FINE_LOCATION");
#endif
        return list;
    }

    int GetSdkInt()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var v = new AndroidJavaClass("android.os.Build$VERSION"))
            return v.GetStatic<int>("SDK_INT");
#else
        return 0;
#endif
    }

    // ===================== Public API =====================
    public void StartFindingPeer()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!HasAllPermissions())
        {
            EnsurePermissions();
            // Permissions are asynchronous on Android. Returning here means the player
            // taps the button again after granting. That's ok and standard.
            return;
        }
        bridge?.CallStatic("startFindingPeer");
#endif
    }

    public void Stop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("stop");
#endif
    }

    public void Send(string line)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("send", line);
#endif
    }

    // ===================== Java -> Unity callbacks =====================
    // Names must match the strings in NearbyConnectionsBridge.UnitySendMessage.

    public void OnNearbyMessage(string line)
    {
        OnMessage?.Invoke(line);
    }

    public void OnNearbyStatus(string statusText)
    {
        Status = statusText;
        Debug.Log("[Nearby] " + statusText);

        if (statusText == "CONNECTED")
        {
            IsConnected = true;
            OnConnected?.Invoke();
        }
        else if (statusText == "DISCONNECTED")
        {
            IsConnected = false;
            OnDisconnected?.Invoke();
        }
        else if (statusText.StartsWith("ERROR"))
        {
            LastError = statusText;
            OnError?.Invoke(statusText);
        }

        OnStatusChanged?.Invoke(statusText);
    }
}
