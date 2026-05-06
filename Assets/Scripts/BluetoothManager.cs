using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// C# façade in front of BluetoothBridge.java.
///
/// MUST live on a GameObject named "BluetoothManager" because the Java side calls
/// UnityPlayer.UnitySendMessage("BluetoothManager", ...).
///
/// The class does three things:
///   1. Forwards Unity calls (StartHost, Connect, Send, ...) to the static
///      Java methods via AndroidJavaClass.
///   2. Receives status / message events from Java and re-broadcasts them as
///      C# events the rest of the game subscribes to.
///   3. Requests the BLUETOOTH_CONNECT runtime permission on first relevant
///      action (Android 12+).
/// </summary>
public class BluetoothManager : MonoBehaviour
{
    public static BluetoothManager Instance { get; private set; }

    public bool   IsConnected { get; private set; }
    public string Status      { get; private set; } = "Bluetooth bereit";
    public string LastError   { get; private set; }

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
        bridge = new AndroidJavaClass("com.example.hybridfight.BluetoothBridge");
#endif
    }

    void Start()
    {
        // Request the runtime permission once at app start so the user has a
        // chance to grant it before tapping any Bluetooth button.
#if UNITY_ANDROID && !UNITY_EDITOR
        const string perm = "android.permission.BLUETOOTH_CONNECT";
        if (!Permission.HasUserAuthorizedPermission(perm))
            Permission.RequestUserPermission(perm);
#endif
    }

    // ===================== Capability queries =====================
    public bool IsSupported()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return bridge != null && bridge.CallStatic<bool>("isSupported");
#else
        return false;
#endif
    }

    public bool IsEnabled()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return bridge != null && bridge.CallStatic<bool>("isEnabled");
#else
        return false;
#endif
    }

    public bool HasConnectPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return bridge != null && bridge.CallStatic<bool>("hasConnectPermission");
#else
        return false;
#endif
    }

    // ===================== System interactions =====================
    public void RequestEnable()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("requestEnable");
#endif
    }

    public void RequestPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("requestConnectPermission");
#endif
    }

    public void OpenSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("openSettings");
#endif
    }

    /// <summary>
    /// Returns paired Bluetooth devices as (displayName, macAddress) pairs.
    /// Empty list if BT is unavailable, off, or permission not granted.
    /// </summary>
    public List<PairedDevice> GetPairedDevices()
    {
        var result = new List<PairedDevice>();
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bridge == null) return result;
        string raw = bridge.CallStatic<string>("getPairedDevicesPipeSeparated");
        if (string.IsNullOrEmpty(raw)) return result;
        foreach (var line in raw.Split('\n'))
        {
            var parts = line.Split('|');
            if (parts.Length == 2) result.Add(new PairedDevice { Name = parts[0], Address = parts[1] });
        }
#endif
        return result;
    }

    // ===================== Connection control =====================
    public void StartHost()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("startHost");
#endif
    }

    public void Connect(string deviceAddress)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("connectTo", deviceAddress);
#endif
    }

    public void Disconnect()
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
    // These are called via UnityPlayer.UnitySendMessage from the Java bridge.

    public void OnBtMessage(string line)
    {
        OnMessage?.Invoke(line);
    }

    public void OnBtStatus(string statusText)
    {
        Status = statusText;
        Debug.Log("[BT] " + statusText);

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

public struct PairedDevice
{
    public string Name;
    public string Address;
}
