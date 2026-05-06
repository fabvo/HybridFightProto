package com.example.hybridfight;

import android.app.Activity;
import android.os.Build;
import android.util.Log;

import com.google.android.gms.nearby.Nearby;
import com.google.android.gms.nearby.connection.AdvertisingOptions;
import com.google.android.gms.nearby.connection.ConnectionInfo;
import com.google.android.gms.nearby.connection.ConnectionLifecycleCallback;
import com.google.android.gms.nearby.connection.ConnectionResolution;
import com.google.android.gms.nearby.connection.ConnectionsClient;
import com.google.android.gms.nearby.connection.DiscoveredEndpointInfo;
import com.google.android.gms.nearby.connection.DiscoveryOptions;
import com.google.android.gms.nearby.connection.EndpointDiscoveryCallback;
import com.google.android.gms.nearby.connection.Payload;
import com.google.android.gms.nearby.connection.PayloadCallback;
import com.google.android.gms.nearby.connection.PayloadTransferUpdate;
import com.google.android.gms.nearby.connection.Strategy;

import com.unity3d.player.UnityPlayer;

import java.nio.charset.StandardCharsets;
import java.util.Random;

/**
 * Bridge to Google Nearby Connections, exposed to Unity as static methods.
 *
 * Usage flow:
 *   Both phones call startFindingPeer(). Each phone simultaneously advertises
 *   AND discovers, both using SERVICE_ID. When one device sees the other, it
 *   sends a connection request; both sides auto-accept (the shared SERVICE_ID
 *   guarantees they are running the same game). Once connected, both stop
 *   advertising and discovering and exchange byte payloads.
 *
 * Why P2P_POINT_TO_POINT?
 *   We're a 1-vs-1 game. P2P_POINT_TO_POINT gives the highest bandwidth and
 *   simplest topology. Switch to P2P_STAR or P2P_CLUSTER for >2 players later.
 *
 * Why simultaneous advertise + discover?
 *   It removes the host/client distinction from the user's perspective:
 *   both players just press "FIND PEER" and the API resolves who connects to
 *   whom. Nearby Connections handles the duplicate-request edge case
 *   internally (the connection still establishes once).
 *
 * The C# receiver GameObject is named "NearbyManager".
 */
public class NearbyConnectionsBridge {

    private static final String TAG        = "NearbyBridge";
    private static final String SERVICE_ID = "com.example.hybridfight";
    private static final Strategy STRATEGY = Strategy.P2P_POINT_TO_POINT;

    private static volatile String  connectedEndpointId;
    private static volatile String  localName;
    private static volatile boolean isSearching;

    private static Activity activity()       { return UnityPlayer.currentActivity; }
    private static ConnectionsClient client(){ return Nearby.getConnectionsClient(activity()); }

    // ===================== Public API (called from Unity) =====================

    public static void startFindingPeer() {
        if (isSearching || connectedEndpointId != null) {
            Log.d(TAG, "Already searching or connected, ignored.");
            return;
        }

        // Friendly local name shown in connection initiated callbacks.
        // We append a random suffix so two devices of the same model are distinguishable.
        localName = Build.MODEL + "-" + new Random().nextInt(10000);
        isSearching = true;
        sendStatus("Suche Mitspieler...");

        AdvertisingOptions adOpts =
                new AdvertisingOptions.Builder().setStrategy(STRATEGY).build();
        client().startAdvertising(localName, SERVICE_ID, connectionCallback, adOpts)
                .addOnSuccessListener(unused -> Log.d(TAG, "startAdvertising OK"))
                .addOnFailureListener(e -> {
                    Log.w(TAG, "startAdvertising failed", e);
                    sendStatus("ERROR Advertising: " + e.getMessage());
                    isSearching = false;
                });

        DiscoveryOptions discOpts =
                new DiscoveryOptions.Builder().setStrategy(STRATEGY).build();
        client().startDiscovery(SERVICE_ID, discoveryCallback, discOpts)
                .addOnSuccessListener(unused -> Log.d(TAG, "startDiscovery OK"))
                .addOnFailureListener(e -> {
                    Log.w(TAG, "startDiscovery failed", e);
                    sendStatus("ERROR Discovery: " + e.getMessage());
                    isSearching = false;
                });
    }

    public static void stop() {
        try {
            client().stopAllEndpoints();
            client().stopAdvertising();
            client().stopDiscovery();
        } catch (Throwable t) {
            Log.w(TAG, "stop() threw: " + t);
        }
        connectedEndpointId = null;
        isSearching = false;
    }

    public static void send(String message) {
        if (connectedEndpointId == null || message == null) return;
        try {
            Payload payload = Payload.fromBytes(message.getBytes(StandardCharsets.UTF_8));
            client().sendPayload(connectedEndpointId, payload);
        } catch (Throwable t) {
            Log.w(TAG, "send failed: " + t);
        }
    }

    // ===================== Callbacks (Java -> Java -> Unity) =====================

    private static final EndpointDiscoveryCallback discoveryCallback = new EndpointDiscoveryCallback() {
        @Override
        public void onEndpointFound(String endpointId, DiscoveredEndpointInfo info) {
            Log.d(TAG, "Endpoint found: " + endpointId + " name=" + info.getEndpointName());
            sendStatus("Mitspieler gefunden, verbinde...");
            // Both sides may call requestConnection; the API handles the merge.
            client().requestConnection(localName, endpointId, connectionCallback)
                    .addOnFailureListener(e -> Log.w(TAG, "requestConnection failed: " + e));
        }

        @Override
        public void onEndpointLost(String endpointId) {
            Log.d(TAG, "Endpoint lost: " + endpointId);
        }
    };

    private static final ConnectionLifecycleCallback connectionCallback = new ConnectionLifecycleCallback() {
        @Override
        public void onConnectionInitiated(String endpointId, ConnectionInfo info) {
            Log.d(TAG, "Connection initiated with " + endpointId
                    + " (" + info.getEndpointName() + ", token=" + info.getAuthenticationDigits() + ")");
            // Auto-accept: same SERVICE_ID means same app. For higher security a future
            // version could show the auth digits to both players for confirmation
            // (Spaceteam-style "are these the same?" tap).
            client().acceptConnection(endpointId, payloadCallback);
        }

        @Override
        public void onConnectionResult(String endpointId, ConnectionResolution result) {
            if (result.getStatus().isSuccess()) {
                Log.i(TAG, "Connected to " + endpointId);
                connectedEndpointId = endpointId;
                isSearching = false;
                // Stop searching now that we have our partner.
                client().stopAdvertising();
                client().stopDiscovery();
                sendStatus("CONNECTED");
            } else {
                String msg = result.getStatus().getStatusMessage();
                Log.w(TAG, "Connection failed: " + result.getStatus());
                sendStatus("Verbindung fehlgeschlagen: " + (msg != null ? msg : result.getStatus().toString()));
            }
        }

        @Override
        public void onDisconnected(String endpointId) {
            Log.i(TAG, "Disconnected from " + endpointId);
            connectedEndpointId = null;
            sendStatus("DISCONNECTED");
        }
    };

    private static final PayloadCallback payloadCallback = new PayloadCallback() {
        @Override
        public void onPayloadReceived(String endpointId, Payload payload) {
            if (payload.getType() != Payload.Type.BYTES) return;
            byte[] bytes = payload.asBytes();
            if (bytes == null) return;
            String msg = new String(bytes, StandardCharsets.UTF_8);
            UnityPlayer.UnitySendMessage("NearbyManager", "OnNearbyMessage", msg);
        }

        @Override
        public void onPayloadTransferUpdate(String endpointId, PayloadTransferUpdate update) {
            // We only send small BYTES payloads; nothing to track.
        }
    };

    private static void sendStatus(String s) {
        Log.d(TAG, "Status -> " + s);
        try {
            UnityPlayer.UnitySendMessage("NearbyManager", "OnNearbyStatus", s);
        } catch (Throwable ignored) { }
    }
}
