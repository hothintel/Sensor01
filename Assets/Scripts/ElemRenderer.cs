using UnityEngine;
using System.Linq;

using System.Collections;
using System.Collections.Generic;
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

public class ElemRenderer : MonoBehaviour
{
    Mesh mesh;

    private void Awake()
    {
    }

    // Use this for initialization
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
    }

    public void UpdateMesh(Vector3[] arrVertices, int nPointsToRender, int nPointsRendered, Color pointColor, Vector3 closestPoint)
    {
        int nPoints;

        if (arrVertices == null)
            nPoints = 0;
        else
            nPoints = System.Math.Min(nPointsToRender, arrVertices.Length - nPointsRendered);
        nPoints = System.Math.Min(nPoints, 65535);

        Vector3[] points = arrVertices.Skip(nPointsRendered).Take(nPoints).ToArray();
        int[] indices = new int[nPoints];
        Color[] colors = new Color[nPoints];

        for (int i = 0; i < nPoints; i++)
        {
            //points[i] = arrVertices[nPointsRendered + i];
            indices[i] = i;
            //colors[i] = pointColor;
            colors[i] = SetDepthColorClosest(points[i], closestPoint);
        }

        if (mesh != null)
            Destroy(mesh);
        mesh = new Mesh();
        mesh.vertices = points;
        mesh.colors = colors;
        mesh.SetIndices(indices, MeshTopology.Points, 0);
        GetComponent<MeshFilter>().mesh = mesh;
    }

    private Color SetDepthColorClosest(Vector3 point, Vector3 closestPoint)
    {
        var camPos = CameraCache.Main.transform.position;

        Color color = SetColor(255, 0, 0); //--- Red

        try
        {
            var distToCam = Vector3.Distance(camPos, point);
            var distClosestPoint = Vector3.Distance(camPos, closestPoint);
            var dist = distToCam - distClosestPoint;

            if (dist > 0.15f)
            {
                color = SetColor(0, 0, 0, 0); //--- Black
            }
            else if (dist > 0.125f)
            {
                color = SetColor(75, 0, 130); //--- Indego
            }
            else if (dist > 0.1f)
            {
                color = SetColor(0, 0, 255); //--- Blue
            }
            else if (dist > 0.075f)
            {
                color = SetColor(0, 128, 0); //--- Green
            }
            else if (dist > 0.05f)
            {
                color = SetColor(255, 255, 0); //--- Yellow
            }
            else if (dist > 0.025f)
            {
                color = SetColor(255, 165, 0); //--- Orange
            }
        }
        catch (Exception)
        {

        }

        return color;
    }

    private static Color SetColor(byte red, byte green, byte blue, byte opacity = 255)
    {
        return new Color32(red, green, blue, opacity);
    }
}
