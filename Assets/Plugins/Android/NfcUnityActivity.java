package com.example.hybridfight;

import android.nfc.NfcAdapter;
import android.nfc.Tag;
import android.nfc.TagLostException;
import android.nfc.tech.NfcA;
import android.os.Bundle;
import android.util.Log;

import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerGameActivity;

import java.io.IOException;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Unity 6 GameActivity-compatible NFC bridge using ReaderMode + active presence polling.
 *
 * Why active polling and not just ReaderMode?
 *
 *   ReaderMode's onTagDiscovered() fires ONCE when a tag enters the field. The OS does
 *   poll internally (EXTRA_READER_PRESENCE_CHECK_DELAY) but does NOT call our callback
 *   repeatedly. There is also no "tag removed" callback. The standard workaround is:
 *
 *     1. On discovery, open an NfcA connection in a worker thread.
 *     2. Loop: send a harmless transceive command (e.g. NTAG GET_VERSION 0x60).
 *        - Success -> tag is still in the field. Tell Unity.
 *        - TagLostException -> tag was lifted. Exit loop, tell Unity.
 *     3. Unity-side has a 0.6s freshness window that flips TagPresent back to false.
 *
 * C# entry points:
 *   UnityPlayer.UnitySendMessage("NfcManager", "OnNfcTagDiscovered", uidHex);
 *   UnityPlayer.UnitySendMessage("NfcManager", "OnNfcStatus",        statusMessage);
 */
public class NfcUnityActivity extends UnityPlayerGameActivity
        implements NfcAdapter.ReaderCallback {

    private static final String TAG = "NfcUnityActivity";
    private static final int    PRESENCE_POLL_MS = 150;

    private NfcAdapter nfcAdapter;

    /** Currently-active watcher thread, if any. */
    private final AtomicReference<Thread> watchThread = new AtomicReference<>();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        nfcAdapter = NfcAdapter.getDefaultAdapter(this);
        if (nfcAdapter == null) {
            Log.w(TAG, "Device has no NFC adapter.");
            sendStatusToUnity("ERROR no NFC adapter on device");
            return;
        }
        if (!nfcAdapter.isEnabled()) {
            Log.w(TAG, "NFC adapter is OFF in system settings.");
            sendStatusToUnity("ERROR NFC turned off in settings");
        } else {
            Log.i(TAG, "NFC adapter ready.");
            sendStatusToUnity("READY");
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (nfcAdapter == null || !nfcAdapter.isEnabled()) return;

        int flags = NfcAdapter.FLAG_READER_NFC_A
                  | NfcAdapter.FLAG_READER_NFC_B
                  | NfcAdapter.FLAG_READER_NFC_F
                  | NfcAdapter.FLAG_READER_NFC_V
                  | NfcAdapter.FLAG_READER_NFC_BARCODE
                  | NfcAdapter.FLAG_READER_NO_PLATFORM_SOUNDS
                  | NfcAdapter.FLAG_READER_SKIP_NDEF_CHECK;

        Bundle extras = new Bundle();
        extras.putInt(NfcAdapter.EXTRA_READER_PRESENCE_CHECK_DELAY, 100);

        nfcAdapter.enableReaderMode(this, this, flags, extras);
        Log.d(TAG, "ReaderMode enabled.");
    }

    @Override
    protected void onPause() {
        super.onPause();
        stopWatcher();
        if (nfcAdapter != null) {
            try { nfcAdapter.disableReaderMode(this); } catch (Throwable ignored) {}
        }
    }

    @Override
    public void onTagDiscovered(Tag tag) {
        if (tag == null) return;
        // If a previous tag is still being watched, kill that thread first --
        // either the player swapped tags or this is a re-detection.
        stopWatcher();
        final Tag captured = tag;
        Thread t = new Thread(() -> watchTag(captured), "NfcWatcher");
        watchThread.set(t);
        t.start();
    }

    private void stopWatcher() {
        Thread t = watchThread.getAndSet(null);
        if (t != null && t != Thread.currentThread()) t.interrupt();
    }

    /**
     * Worker thread. Holds an NfcA connection to the tag and pings it
     * every PRESENCE_POLL_MS to detect when it's lifted.
     */
    private void watchTag(Tag tag) {
        String uid = uidToHex(tag.getId());
        // Always send the initial discovery event so the C# side reacts ASAP
        // even if we then fail to open a continuous connection.
        sendTagToUnity(uid);

        NfcA nfcA = NfcA.get(tag);
        if (nfcA == null) {
            Log.w(TAG, "Tag is not NfcA-compatible, no continuous presence tracking. UID=" + uid);
            return;
        }

        try {
            nfcA.connect();
            Log.d(TAG, "NfcA connected, watching presence (UID=" + uid + ")");

            while (!Thread.currentThread().isInterrupted()) {
                // Heartbeat: tell Unity the tag is still here.
                sendTagToUnity(uid);

                Thread.sleep(PRESENCE_POLL_MS);

                if (!nfcA.isConnected()) {
                    Log.d(TAG, "Connection no longer alive, ending watch.");
                    break;
                }

                try {
                    // GET_VERSION (0x60). NTAG21x and Mifare Ultralight EV1 support this.
                    // For other NfcA tags it might respond with an error -- that's fine,
                    // an error reply still proves the tag is in the field. Only
                    // TagLostException means the tag is physically gone.
                    nfcA.transceive(new byte[]{(byte) 0x60});
                } catch (TagLostException e) {
                    Log.d(TAG, "Tag was lifted (TagLostException). UID=" + uid);
                    break;
                } catch (IOException e) {
                    // Tag responded with an error but is still in the field.
                    // Verify by checking connection state.
                    if (!nfcA.isConnected()) {
                        Log.d(TAG, "Connection broken after IOException. UID=" + uid);
                        break;
                    }
                    // else: tag responded "command not supported" but is still here.
                    // Continue the loop, on next iteration we'll retry and the heartbeat
                    // event has already been sent.
                }
            }
        } catch (InterruptedException ie) {
            // Expected on disable / new tag.
        } catch (IOException e) {
            Log.w(TAG, "NfcA.connect() failed: " + e);
        } finally {
            try { nfcA.close(); } catch (Throwable ignored) {}
            Log.d(TAG, "Watcher thread for UID=" + uid + " terminated.");
        }
    }

    private void sendTagToUnity(String uid) {
        UnityPlayer.UnitySendMessage("NfcManager", "OnNfcTagDiscovered", uid);
    }

    private void sendStatusToUnity(String status) {
        try {
            UnityPlayer.UnitySendMessage("NfcManager", "OnNfcStatus", status);
        } catch (Throwable t) {
            Log.w(TAG, "Could not deliver status to Unity yet: " + status);
        }
    }

    private static String uidToHex(byte[] id) {
        StringBuilder sb = new StringBuilder(id.length * 2);
        for (byte b : id) sb.append(String.format("%02X", b));
        return sb.toString();
    }
}
