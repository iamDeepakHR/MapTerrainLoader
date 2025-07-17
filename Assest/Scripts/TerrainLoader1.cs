using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System;

public class TerrainLoader1 : MonoBehaviour
{
    public InputField placeInput;
    public Button searchButton;

    public string mapboxToken = "pk.eyJ1IjoiZGVlcHUwNiIsImEiOiJjbWQ2ODd5Z3IwN2E5Mm5xeHRwODRmb3g2In0.jbDkLHq97k6rtlX_QVyDxw";  // Replace with your actual Mapbox token
    public int zoom = 14;

    void Start()    
    {
        if (Camera.main != null)
        {
            Camera.main.depthTextureMode |= DepthTextureMode.Depth;
        }
    if (placeInput == null || searchButton == null)
            {
                Debug.LogWarning("placeInput or searchButton not assigned in Inspector.");
                return;
            }

    searchButton.onClick.AddListener(() =>
    {
        string placeName = placeInput.text;
        StartCoroutine(GetCoordinatesFromPlace(placeName));
    });
}

    IEnumerator GetCoordinatesFromPlace(string place)
    {
        string placeEncoded = UnityWebRequest.EscapeURL(place);
        string url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{placeEncoded}.json?access_token={mapboxToken}";

        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Geocoding error: " + www.error);
        }
        else
        {
            var json = www.downloadHandler.text;
            var response = JsonUtility.FromJson<GeocodingResponse>("{\"features\":" + ExtractArray(json, "features") + "}");

            if (response.features.Length > 0)
            {
                double lon = response.features[0].center[0];
                double lat = response.features[0].center[1];
                StartCoroutine(DownloadAndGenerateTerrain(lat, lon));
            }
            else
            {
                Debug.LogWarning("Place not found!");
            }
        }
    }

    IEnumerator DownloadAndGenerateTerrain(double lat, double lon)
    {
        int tileX = (int)((lon + 180.0) / 360.0 * (1 << zoom));
        int tileY = LatLonToTileY(lat, zoom);

        // Download elevation texture
        string terrainUrl = $"https://api.mapbox.com/v4/mapbox.terrain-rgb/{zoom}/{tileX}/{tileY}@2x.pngraw?access_token={mapboxToken}";
        UnityWebRequest terrainReq = UnityWebRequestTexture.GetTexture(terrainUrl);
        yield return terrainReq.SendWebRequest();

        if (terrainReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Terrain download failed: " + terrainReq.error);
            yield break;
        }

        Texture2D elevationTexture = DownloadHandlerTexture.GetContent(terrainReq);

        // Download satellite texture
        string imageUrl = $"https://api.mapbox.com/styles/v1/mapbox/satellite-v9/tiles/512/{zoom}/{tileX}/{tileY}?access_token={mapboxToken}";
        UnityWebRequest imageReq = UnityWebRequestTexture.GetTexture(imageUrl);
        yield return imageReq.SendWebRequest();

        Texture2D satelliteTexture = null;
        if (imageReq.result == UnityWebRequest.Result.Success)
        {
            satelliteTexture = DownloadHandlerTexture.GetContent(imageReq);
            Debug.Log("✅ Satellite image loaded");
        }
        else
        {
            Debug.LogError("❌ Failed to load satellite image: " + imageReq.error);
        }

        GenerateMeshFromRGB(elevationTexture, satelliteTexture);
    }

    int LatLonToTileY(double lat, int zoom)
    {
        lat = Math.Max(-85.05112878, Math.Min(85.05112878, lat));
        double latRad = lat * Math.PI / 180.0;
        double n = Math.Log(Math.Tan(Math.PI / 4 + latRad / 2));
        return (int)((1.0 - n / Math.PI) / 2.0 * (1 << zoom));
    }

    void GenerateMeshFromRGB(Texture2D elevationTex, Texture2D satelliteTex)
    {
        int width = elevationTex.width;
        int height = elevationTex.height;

        Vector3[] vertices = new Vector3[width * height];
        Vector2[] uv = new Vector2[width * height];
        int[] triangles = new int[(width - 1) * (height - 1) * 6];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color rgb = elevationTex.GetPixel(x, y);
                float elevation = -10000 + ((rgb.r * 255 * 256 * 256 + rgb.g * 255 * 256 + rgb.b * 255) * 0.1f);
                vertices[y * width + x] = new Vector3(x, elevation * 0.01f, y);
                uv[y * width + x] = new Vector2((float)x / width, (float)y / height);
            }
        }

        int t = 0;
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int i = y * width + x;
                triangles[t++] = i;
                triangles[t++] = i + width;
                triangles[t++] = i + width + 1;

                triangles[t++] = i;
                triangles[t++] = i + width + 1;
                triangles[t++] = i + 1;
            }
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        GameObject terrain = new GameObject("GeneratedTerrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        terrain.GetComponent<MeshFilter>().mesh = mesh;

        // ✅ Fix pink shader by trying URP or Standard
        Material mat = null;

        // Try URP shader first (if you're using URP)
        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        Shader builtinShader = Shader.Find("Standard");

        if (urpShader != null)
            mat = new Material(urpShader);
        else if (builtinShader != null)
            mat = new Material(builtinShader);
        else
            mat = new Material(Shader.Find("Diffuse")); // fallback

        if (satelliteTex != null)
            mat.mainTexture = satelliteTex;
        else
            mat.color = Color.gray;

        terrain.GetComponent<MeshRenderer>().material = mat;
        terrain.GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    [System.Serializable]
    public class GeocodingFeature { public float[] center; }
    [System.Serializable]
    public class GeocodingResponse { public GeocodingFeature[] features; }

    string ExtractArray(string json, string key)
    {
        int index = json.IndexOf($"\"{key}\":[");
        if (index == -1) return "[]";

        int startIndex = json.IndexOf("[", index);
        int bracketCount = 0;
        for (int i = startIndex; i < json.Length; i++)
        {
            if (json[i] == '[') bracketCount++;
            if (json[i] == ']') bracketCount--;
            if (bracketCount == 0)
            {
                return json.Substring(startIndex, i - startIndex + 1);
            }
        }
        return "[]";
    }
}
