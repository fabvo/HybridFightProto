using UnityEngine;

/// <summary>
/// Receives NFC tag detection messages from the Android Java plugin
/// (NfcUnityActivity.java -> UnityPlayer.UnitySendMessage).
///
/// This component MUST sit on a GameObject named exactly "NfcManager" because
/// UnitySendMessage looks the receiver up by GameObject name.
///
/// Android does not fire a "tag removed" event. We therefore expose
/// LastTagId / LastTagTime / RecentlyDetected and let the GameManager combine
/// that with the phone's orientation to decide if the player is currently focusing.
/// </summary>
public class NfcManager : MonoBehaviour
{
    public string LastTagId        { get; private set; }
    public float  LastTagTime      { get; private set; } = -999f;
    public int    TagsDetectedCount{ get; private set; }
    public string LastError        { get; private set; }

    public event System.Action<string> OnTagDiscovered;
    public event System.Action<string> OnPluginError;

    /// <summary>True if a tag has been seen within the last <paramref name="windowSeconds"/> seconds.</summary>
    public bool RecentlyDetected(float windowSeconds = 600f)
        => LastTagId != null && Time.time - LastTagTime < windowSeconds;

    public float SecondsSinceLastTag => Time.time - LastTagTime;

    // -- Methods called from Java plugin via UnitySendMessage("NfcManager", ...) --

    public void OnNfcTagDiscovered(string tagId)
    {
        LastTagId = tagId;
        LastTagTime = Time.time;
        TagsDetectedCount++;
        Debug.Log($"[NFC] Tag #{TagsDetectedCount} detected: {tagId}");
        OnTagDiscovered?.Invoke(tagId);
    }

    public void OnNfcStatus(string statusMessage)
    {
        Debug.Log($"[NFC] Status: {statusMessage}");
        if (statusMessage.StartsWith("ERROR"))
        {
            LastError = statusMessage;
            OnPluginError?.Invoke(statusMessage);
        }
    }
}
