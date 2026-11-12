using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays JPEG webcam frames sent by the vision backend on UDP 5056.</summary>
public class UDPWebcamReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private int port = 5056;
    [Header("UI Reference")]
    [SerializeField] private RawImage miniMapRawImage;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool isRunning;
    private Texture2D webcamTexture;
    private byte[] latestFrameBytes;
    private string receiveError;
    private readonly object lockObject = new object();

    void OnEnable()
    {
        if(RedboolPvp.Active) {if(miniMapRawImage!=null) miniMapRawImage.gameObject.SetActive(false);enabled=false;return;}
        webcamTexture = new Texture2D(320, 240, TextureFormat.RGB24, false);
        if (miniMapRawImage != null) miniMapRawImage.texture = webcamTexture;
        try
        {
            udpClient = new UdpClient(port);
            isRunning = true;
            var socket = udpClient;
            receiveThread = new Thread(() => ReceiveData(socket)) { IsBackground = true, Name = "VisionStriker Webcam" };
            receiveThread.Start();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UDP Webcam] Cannot listen on {port}: {ex.Message}");
            enabled = false;
        }
    }

    private void ReceiveData(UdpClient socket)
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        while (isRunning)
        {
            try
            {
                byte[] data = socket.Receive(ref remoteEP);
                if (data.Length > 0) lock (lockObject) latestFrameBytes = data;
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                if (isRunning) lock (lockObject) receiveError = ex.Message;
                break;
            }
        }
    }

    void Update()
    {
        byte[] frame;
        string error;
        lock (lockObject)
        {
            frame = latestFrameBytes; latestFrameBytes = null;
            error = receiveError; receiveError = null;
        }
        if (error != null) Debug.LogWarning("[UDP Webcam] Receive stopped: " + error);
        // Unity texture decoding must remain on the main thread.
        if (frame != null && webcamTexture != null) webcamTexture.LoadImage(frame);
    }

    void OnDisable() { StopReceiver(); }
    void OnDestroy() { StopReceiver(); }
    private void StopReceiver()
    {
        isRunning = false;
        udpClient?.Close();
        udpClient = null;
        // Closing the socket wakes Receive; avoid unsupported Thread.Abort on scene changes.
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(500);
        receiveThread = null;
        lock (lockObject) { latestFrameBytes = null; receiveError = null; }
        if (webcamTexture != null)
        {
            if (miniMapRawImage != null && miniMapRawImage.texture == webcamTexture) miniMapRawImage.texture = null;
            Destroy(webcamTexture);
            webcamTexture = null;
        }
    }
}
