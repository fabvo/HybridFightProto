package com.example.hybridfight;

import android.nfc.NdefMessage;
import android.nfc.NdefRecord;
import android.nfc.NfcAdapter;
import android.nfc.Tag;
import android.nfc.TagLostException;
import android.nfc.tech.Ndef;
import android.nfc.tech.NfcA;
import android.os.Bundle;
import android.util.Log;

import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerGameActivity;

import java.io.IOException;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Unity 6 GameActivity NFC bridge with ReaderMode, active presence polling,
 * AND NDEF text record reading.
 *
 * Discovery flow:
 *   1. ReaderMode fires onTagDiscovered (NDEF is pre-read by the system since
 *      we do NOT set FLAG_READER_SKIP_NDEF_CHECK).
 *   2. We extract the first TEXT record from the cached NDEF message, if any.
 *   3. We send "uid|ndefPayload" to Unity on every heartbeat.
 *   4. A worker thread opens NfcA and polls for presence via GET_VERSION (0x60).
 *
 * Tag writing:
 *   Use NFC Tools PRO -> Write -> Text -> type FOCUS / HEAL / SHIELD / BOMB.
 *   Tags without any NDEF record default to FOCUS in the C# TagEffects parser.
 */
public class NfcUnityActivity extends UnityPlayerGameActivity
        implements NfcAdapter.ReaderCallback {

    private static final String TAG = "NfcUnityActivity";
    private static final int    PRESENCE_POLL_MS = 150;

    private NfcAdapter nfcAdapter;
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

        // No FLAG_READER_SKIP_NDEF_CHECK: let the system pre-read NDEF so we
        // can use getCachedNdefMessage() without opening a separate connection.
        int flags = NfcAdapter.FLAG_READER_NFC_A
                  | NfcAdapter.FLAG_READER_NFC_B
                  | NfcAdapter.FLAG_READER_NFC_F
                  | NfcAdapter.FLAG_READER_NFC_V
                  | NfcAdapter.FLAG_READER_NFC_BARCODE
                  | NfcAdapter.FLAG_READER_NO_PLATFORM_SOUNDS;

        Bundle extras = new Bundle();
        extras.putInt(NfcAdapter.EXTRA_READER_PRESENCE_CHECK_DELAY, 100);

        nfcAdapter.enableReaderMode(this, this, flags, extras);
        Log.d(TAG, "ReaderMode enabled (with NDEF reading).");
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
        stopWatcher();

        // Read NDEF text payload before starting the NfcA presence watcher.
        String ndefPayload = readNdefText(tag);

        final Tag captured = tag;
        final String payload = ndefPayload;
        Thread t = new Thread(() -> watchTag(captured, payload), "NfcWatcher");
        watchThread.set(t);
        t.start();
    }

    /**
     * Extracts the first NDEF TEXT record as a String.
     * Returns "" if the tag has no NDEF data or no text record.
     */
    private String readNdefText(Tag tag) {
        Ndef ndef = Ndef.get(tag);
        if (ndef == null) return "";

        NdefMessage msg = ndef.getCachedNdefMessage();
        if (msg == null) {
            // Fallback: manually connect and read (some ReaderMode implementations
            // don't always populate the cache).
            try {
                ndef.connect();
                msg = ndef.getNdefMessage();
            } catch (Exception e) {
                Log.w(TAG, "NDEF manual read failed: " + e);
            } finally {
                try { ndef.close(); } catch (Exception ignored) {}
            }
        }
        if (msg == null) return "";

        for (NdefRecord rec : msg.getRecords()) {
            if (rec.getTnf() == NdefRecord.TNF_WELL_KNOWN
                    && java.util.Arrays.equals(rec.getType(), NdefRecord.RTD_TEXT)) {
                try {
                    byte[] payload = rec.getPayload();
                    if (payload == null || payload.length < 2) continue;
                    // Status byte: bit 7 = encoding (0=UTF-8, 1=UTF-16),
                    //               bits 0..5 = language code length.
                    int langLen = payload[0] & 0x3F;
                    if (1 + langLen >= payload.length) continue;
                    return new String(payload, 1 + langLen, payload.length - 1 - langLen, "UTF-8");
                } catch (Exception e) {
                    Log.w(TAG, "NDEF text decode failed: " + e);
                }
            }
        }
        return "";
    }

    private void stopWatcher() {
        Thread t = watchThread.getAndSet(null);
        if (t != null && t != Thread.currentThread()) t.interrupt();
    }

    /**
     * Worker thread: polls NfcA presence and sends heartbeats to Unity.
     * Message format: "uid|ndefPayload" (pipe-separated).
     */
    private void watchTag(Tag tag, String ndefPayload) {
        String uid = uidToHex(tag.getId());
        String message = uid + "|" + ndefPayload;
        sendTagToUnity(message);

        NfcA nfcA = NfcA.get(tag);
        if (nfcA == null) {
            Log.w(TAG, "Tag is not NfcA-compatible, no presence tracking. UID=" + uid);
            return;
        }

        try {
            nfcA.connect();
            Log.d(TAG, "NfcA connected, watching presence (UID=" + uid
                    + ", NDEF=" + ndefPayload + ")");

            while (!Thread.currentThread().isInterrupted()) {
                sendTagToUnity(message);
                Thread.sleep(PRESENCE_POLL_MS);

                if (!nfcA.isConnected()) break;

                try {
                    nfcA.transceive(new byte[]{(byte) 0x60});
                } catch (TagLostException e) {
                    Log.d(TAG, "Tag lifted. UID=" + uid);
                    break;
                } catch (IOException e) {
                    if (!nfcA.isConnected()) break;
                }
            }
        } catch (InterruptedException ie) {
            // Expected on disable / new tag.
        } catch (IOException e) {
            Log.w(TAG, "NfcA.connect() failed: " + e);
        } finally {
            try { nfcA.close(); } catch (Throwable ignored) {}
            Log.d(TAG, "Watcher for UID=" + uid + " done.");
        }
    }

    private void sendTagToUnity(String msg) {
        UnityPlayer.UnitySendMessage("NfcManager", "OnNfcTagDiscovered", msg);
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
