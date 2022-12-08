using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Runtime.InteropServices;
using TMPro;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Utilities;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.UI.BoundsControl;
using Microsoft.MixedReality.Toolkit.UI;
using Microsoft.MixedReality.Toolkit.Audio;
using System.Text;
using Microsoft.MixedReality.OpenXR;
using Unity.VisualScripting;
using UnityEngine.SceneManagement;

#if ENABLE_WINMD_SUPPORT
using Windows.Perception.Spatial;
using HL2UnityPlugin;
#endif

public class ResearchManager : MonoBehaviour, IMixedRealitySpeechHandler
{
#if ENABLE_WINMD_SUPPORT
    HL2ResearchMode researchMode;
#endif

    bool enablePointCloud = true;

    public TextMeshPro MyInfo;
    public GameObject MyPoint;

    private bool isUpdating = false;

    private GameObject _origin;
    private float _baseLineWidth = 0.001f;
    private float _baseOriginLen = 0.1f;
    private float _baseGradLen = 0.0025f;
    public Color pointColor = Color.white;
    private PointCloudRenderer _pointCloudRenderer;
    public GameObject PointCloudRendererGo;

    private ConcurrentQueue<string> ErrorQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<List<Vector3>> SensorQueue = new ConcurrentQueue<List<Vector3>>();
    private ConcurrentQueue<Vector3> SensorCoordQueue = new ConcurrentQueue<Vector3>();
    private List<Vector3> CurrentSensorList = new List<Vector3>();
    private Vector3 CurrentClosePoint = Vector3.zero;

    private float[] currentBuffer = new float[] { };
    private Vector3 _curCamPosition = Vector3.zero;
    private Vector3 _curForward = Vector3.zero;
    private float _calibrate = -0.02f;
    private bool _renderPointCloud = true;
    private Vector3 _camOffset = Vector3.zero;

#if ENABLE_WINMD_SUPPORT
    Windows.Perception.Spatial.SpatialCoordinateSystem unityWorldOrigin;
#endif

    void Start()
    {
        MyInfo.text = "Starting...\r\n";
        CoreServices.DiagnosticsSystem.ShowProfiler = false;
        CoreServices.InputSystem.RegisterHandler<IMixedRealitySpeechHandler>(this);

        if (PointCloudRendererGo != null)
        {
            _pointCloudRenderer = PointCloudRendererGo.GetComponent<PointCloudRenderer>();
        }

        _camOffset = Vector3.zero;
        //_curForward = CameraCache.Main.transform.forward;
        // create origin axis
        Material whiteMat = new Material(Shader.Find("Standard"));
        whiteMat.SetColor("_Color", Color.white);
        _origin = Helpers.CreateAxis(whiteMat, _baseLineWidth, _baseOriginLen, _baseGradLen);
        _origin.transform.position = new Vector3(0, 0, 0);
        _origin.transform.rotation = Quaternion.identity;

        InitResearchMode();
    }


    private void InitResearchMode()
    {
#if ENABLE_WINMD_SUPPORT
        researchMode = new HL2ResearchMode();
        researchMode.InitializeLongDepthSensor();
        researchMode.InitializeSpatialCamerasFront();

        try
        {
            unityWorldOrigin = PerceptionInterop.GetSceneCoordinateSystem(UnityEngine.Pose.identity) as SpatialCoordinateSystem;

            if (unityWorldOrigin != null)
                MyInfo.text += $"unityWorldOrigin PerceptionInterop={unityWorldOrigin}\r\n";
            else
                unityWorldOrigin = SpatialLocator.GetDefault().CreateStationaryFrameOfReferenceAtCurrentLocation().CoordinateSystem;
 
            if (unityWorldOrigin != null)
                MyInfo.text += $"unityWorldOrigin SpatialLocator={unityWorldOrigin}\r\n";

            researchMode.SetReferenceCoordinateSystem(unityWorldOrigin);
        }
        catch(Exception myEx)
        {
            MyInfo.text += $"myEx={myEx.Message}\r\n";
            unityWorldOrigin=null;
        }

        researchMode.SetPointCloudDepthOffset(0);
        researchMode.StartLongDepthSensorLoop(enablePointCloud);
        researchMode.StartSpatialCamerasFrontLoop();
#endif    

    }

    private int maxFromCenter = 20;

    void Update()
    {
        if (!isUpdating)
        {
#if ENABLE_WINMD_SUPPORT
            if (!researchMode.LongThrowPointCloudUpdated()) return;
#endif
            isUpdating = true;
            if (SensorQueue.Count > 0)
            {
                List<Vector3> dequeuedPoints = new List<Vector3>();
                if (SensorQueue.TryDequeue(out dequeuedPoints))
                {
                    CurrentSensorList = dequeuedPoints;
                }

                Vector3 closePt = Vector3.zero;
                if (SensorCoordQueue.TryDequeue(out closePt))
                    CurrentClosePoint = closePt;
            }

            currentBuffer = new float[] { };
#if ENABLE_WINMD_SUPPORT
            currentBuffer = researchMode.GetLongThrowPointCloudBuffer();
#endif
            _curCamPosition = CameraCache.Main.transform.position;
            //_curForward = CameraCache.Main.transform.forward;

            Thread worker = new Thread(new ThreadStart(UpdateSensorPoints));
            worker.Start();
        }

        MyPoint.transform.position = CurrentClosePoint;

        if (_renderPointCloud)
            _pointCloudRenderer.Render(CurrentSensorList.ToArray(), pointColor, CurrentClosePoint);
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus) StopSensorsEvent();
    }

    public void StopSensorsEvent()
    {
#if ENABLE_WINMD_SUPPORT
        researchMode.StopAllSensorDevice();
#endif
    }

    private void UpdateSensorPoints()
    {
        isUpdating = true;

        float closest = float.MaxValue;

        List<Vector3> tempPoints = new List<Vector3>();
        List<Vector3> sensorPoints = new List<Vector3>();
        Vector3 closestPoint = Vector3.zero;
        //Vector3 deltaError = _curForward * _calibrate;
        try
        {
            if (currentBuffer.Length == 0)
                return;

            int pointCloudLength = currentBuffer.Length / 3;
            for (int i = 0; i < pointCloudLength; i++)
            {
                var x = currentBuffer[3 * i];
                var y = currentBuffer[3 * i + 1];
                var z = currentBuffer[3 * i + 2];
                var curPt = new Vector3(x, y, z);
                //curPt = curPt - _camOffset;

                var dist = Vector3.Distance(_curCamPosition, curPt);
                if (dist < 0.75f)
                {
                    tempPoints.Add(curPt);
                    if (dist < closest)
                    {
                        closest = dist;
                        closestPoint = curPt;
                    }
                }
            }

            for (int j = 0; j < tempPoints.Count; j++)
            {
                var dist = Vector3.Distance(tempPoints[j], closestPoint);
                if (dist < 0.5f)
                    sensorPoints.Add(tempPoints[j]);
            }
        }
        catch (Exception exception)
        {
            string exMsg = "";
            if (exception.Message.Contains("Out of Range"))
                exMsg = "Out of Range";
            else
                exMsg = exception.Message;
            string errMsg = $"Error: ex={exMsg}";
            ErrorQueue.Enqueue(errMsg);
        }
        finally
        {
            if (sensorPoints.Count > 0)
            {
                SensorQueue.Enqueue(sensorPoints);
                SensorCoordQueue.Enqueue(closestPoint);
            }

            isUpdating = false;
        }

    }

    public static Vector3 CenterOfVectors(List<Vector3> vectors)
    {
        Vector3 sum = Vector3.zero;
        if (vectors == null || vectors.Count == 0)
            return sum;

        foreach (Vector3 vec in vectors)
            sum += vec;

        return sum / vectors.Count;
    }

    public static Vector3 GetSmoothedPoint(Vector3 newPoint, ref List<Vector3> myPoints, int maxQueueLength)
    {
        myPoints.Add(newPoint);
        if (myPoints.Count >= maxQueueLength)
        {
            myPoints.RemoveAt(0);
        }

        return CenterOfVectors(myPoints);
    }

    public void OnSpeechKeywordRecognized(SpeechEventData eventData)
    {
        float delta = 0.01f;
        MyInfo.text += $"word={eventData.Command.Keyword.ToLower()} ";

        switch (eventData.Command.Keyword.ToLower())
        {
            case "move up":
                _camOffset = new Vector3(_camOffset.x, _camOffset.y + delta, _camOffset.z);
                break;

            case "move down":
                _camOffset = new Vector3(_camOffset.x, _camOffset.y - delta, _camOffset.z);
                break;

            case "move left":
                _camOffset = new Vector3(_camOffset.x - delta, _camOffset.y, _camOffset.z);

                break;

            case "move right":
                _camOffset = new Vector3(_camOffset.x + delta, _camOffset.y, _camOffset.z);
                break;

            case "move forward":
                _camOffset = new Vector3(_camOffset.x, _camOffset.y, _camOffset.z - delta);
                break;

            case "move back":
                _camOffset = new Vector3(_camOffset.x, _camOffset.y, _camOffset.z + delta);
                break;

            case "add offset":
                _camOffset = CurrentClosePoint;
                break;
        }

        MyInfo.text = $"Offset={_camOffset.x},{_camOffset.y},{_camOffset.z}\r\n";

    }
} // end of class
