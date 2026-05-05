using UnityEngine;

/// <summary>
/// Receives NFC tag detection messages from the Android Java plugin
/// (NfcUnityActivity.java -> UnityPlayer.UnitySendMessage).
///
/// This component MUST sit on a GameObject named exactly "NfcManager" because
/// UnitySendMessage looks the receiver up by GameObject name.
///
/// With ReaderMode, the Java plugin fires OnNfcTagDiscovered roughly every
/// 100-150 ms while a tag sits on the phone. We use that to derive
/// <see cref="TagPresent"/>: true if a discovery event has happened within the
/// last <see cref="PresenceWindow"/> seconds. When the player lifts the phone,
/// no more events come in and TagPresent flips back to false after that window.
/// </summary>
public class NfcManager : MonoBehaviour
{
    /// <summary>
    /// How long after the last detection we still consider the tag "present".
    /// Should be larger than the OS polling interval (~125 ms) plus some jitter.
    /// 0.6 s is a good compromise -- responsive but not flickery.
    /// </summary>
    public const float PresenceWindow = 0.6f;

    public string LastTagId        { get; private set; }
    public float  LastTagTime      { get; private set; } = -999f;
    public int    TagsDetectedCount{ get; private set; }
    public string LastError        { get; private set; }

    /// <summary>True while the tag is physically on the phone (within PresenceWindow seconds of last event).</summary>
    public bool TagPresent => LastTagId != null && Time.time - LastTagTime < PresenceWindow;

    public float SecondsSinceLastTag => Time.time - LastTagTime;

    public event System.Action<string> OnTagDiscovered;
    public event System.Action<string> OnPluginError;

    // -- Methods called from Java plugin via UnitySendMessage("NfcManager", ...) --

    public void OnNfcTagDiscovered(string tagId)
    {
        bool isFirstThisSession = LastTagId == null || !TagPresent;
        LastTagId   = tagId;
        LastTagTime = Time.time;
        if (isFirstThisSession)
        {
            TagsDetectedCount++;
            Debug.Log($"[NFC] Tag #{TagsDetectedCount} placed: {tagId}");
        }
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
