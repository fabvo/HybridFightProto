package com.example.hybridfight;

import android.app.PendingIntent;
import android.content.Intent;
import android.nfc.NfcAdapter;
import android.nfc.Tag;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;

import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerGameActivity;

/**
 * Unity 6 GameActivity-compatible NFC bridge.
 *
 * This version also:
 *   - reads the cold-start intent (in case the OS launched our app via the tag),
 *   - reports adapter status / errors back to Unity (NfcManager.OnNfcStatus) so
 *     you can see in-game whether NFC is even initialized.
 *
 * C# entry points:
 *   UnityPlayer.UnitySendMessage("NfcManager", "OnNfcTagDiscovered", uidHex);
 *   UnityPlayer.UnitySendMessage("NfcManager", "OnNfcStatus",        statusMessage);
 */
public class NfcUnityActivity extends UnityPlayerGameActivity {

    private static final String TAG = "NfcUnityActivity";

    private NfcAdapter   nfcAdapter;
    private PendingIntent nfcPendingIntent;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        nfcAdapter = NfcAdapter.getDefaultAdapter(this);
        if (nfcAdapter == null) {
            Log.w(TAG, "Device has no NFC adapter; the focus mechanic will not work.");
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

        Intent nfcIntent = new Intent(this, getClass())
                .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);

        // PendingIntent.FLAG_MUTABLE was added in API 31 and is required there;
        // older devices don't know the flag.
        int flags;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            flags = PendingIntent.FLAG_MUTABLE;
        } else {
            flags = 0;
        }

        nfcPendingIntent = PendingIntent.getActivity(this, 0, nfcIntent, flags);

        // Cold-start: app was launched by tapping a tag. Pass that intent through
        // immediately so the player gets credit for the focus pose.
        deliverNfcTag(getIntent());
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (nfcAdapter != null && nfcAdapter.isEnabled()) {
            // null filters / null techLists -> receive ALL tags discovered while on top.
            nfcAdapter.enableForegroundDispatch(this, nfcPendingIntent, null, null);
            Log.d(TAG, "Foreground dispatch enabled.");
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        if (nfcAdapter != null) {
            try {
                nfcAdapter.disableForegroundDispatch(this);
            } catch (IllegalStateException e) {
                // Activity not in foreground at this moment, harmless.
            }
        }
    }

    @Override
    public void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        // GameActivity may also keep a reference to the latest intent; we rely on the parameter.
        setIntent(intent);
        deliverNfcTag(intent);
    }

    private void deliverNfcTag(Intent intent) {
        if (intent == null) return;
        Tag tag = intent.getParcelableExtra(NfcAdapter.EXTRA_TAG);
        if (tag == null) return;

        byte[] id = tag.getId();
        StringBuilder sb = new StringBuilder(id.length * 2);
        for (byte b : id) sb.append(String.format("%02X", b));
        String uid = sb.toString();

        Log.i(TAG, "NFC tag detected: " + uid);
        UnityPlayer.UnitySendMessage("NfcManager", "OnNfcTagDiscovered", uid);
    }

    private void sendStatusToUnity(String status) {
        // Unity may not yet be loaded during onCreate; wrap in try/catch to be safe.
        try {
            UnityPlayer.UnitySendMessage("NfcManager", "OnNfcStatus", status);
        } catch (Throwable t) {
            Log.w(TAG, "Could not deliver status to Unity yet: " + status);
        }
    }
}
