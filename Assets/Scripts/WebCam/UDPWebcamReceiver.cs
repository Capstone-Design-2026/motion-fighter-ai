using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class UdpMiniMapReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private int port = 5056;

    [Header("UI Reference")]
    [SerializeField] private RawImage miniMapRawImage;

    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = true;

    private Texture2D webcamTexture;
    private byte[] latestFrameBytes;
    private readonly object lockObject = new object();
    private bool hasNewFrame = false;

    void Start()
    {
        // 320x240 해상도 기본 텍스처 생성
        webcamTexture = new Texture2D(320, 240, TextureFormat.RGB24, false);
        if (miniMapRawImage != null)
        {
            miniMapRawImage.texture = webcamTexture;
        }

        // UDP 수신 쓰레드 시작
        receiveThread = new Thread(ReceiveData) { IsBackground = true };
        receiveThread.Start();
    }

    private void ReceiveData()
    {
        udpClient = new UdpClient(port);
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port);

        while (isRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEP);
                if (data != null && data.Length > 0)
                {
                    lock (lockObject)
                    {
                        latestFrameBytes = data;
                        hasNewFrame = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // 소켓 종료 시 발생하는 예외 무시
                if (!isRunning) break;
                Debug.LogWarning($"[UDP MiniMap] Receive Error: {ex.Message}");
            }
        }
    }

    void Update()
    {
        // 메인 쓰레드에서 텍스처 업데이트
        if (hasNewFrame)
        {
            byte[] frameToLoad = null;

            lock (lockObject)
            {
                frameToLoad = latestFrameBytes;
                hasNewFrame = false;
            }

            if (frameToLoad != null)
            {
                // JPEG 바이트 데이터를 Texture2D로 디코딩 (80% 화질 수용)
                webcamTexture.LoadImage(frameToLoad);
            }
        }
    }

    private void OnDestroy()
    {
        isRunning = false;
        if (udpClient != null)
        {
            udpClient.Close();
        }
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Abort();
        }
    }
}