using UnityEngine;
using System.Collections.Generic;

public class PerlinNoise : MonoBehaviour
{
    // === PARÂMETROS DO TERRENO ===
    public int width = 256;
    public int height = 256;
    public float depth = 12;
    public float scale = 12f;
    public int octaves = 8;
    public float persistence = 0.5f;

    // Offsets (calculados aleatoriamente)
    private float offsetX;
    private float offsetY;
    private float rockOffset;

    // === PARÂMETROS DE TEXTURA ===
    public TerrainLayer[] terrainLayers;
    public float textureScale = 60;
    public int textureOctaves = 16;
    public float texturePersistence = 0.5f;
    public float rockDensity = 1.0f;

    // === PARÂMETROS DE ÁRVORES ===
    public float treePerlinScale = 40f;
    public float treePerlinThreshold = 0.5f;
    public float treePlacementSlopeLimit = 30f;
    public float treePlacementHeight = 0.1f;
    public int treeCount = 5000;

    // === PARÂMETROS DA EROSÃO HIDRÁULICA ===
    public bool enableErosion = true;
    public int erosionIterations = 50000;
    public int maxDropletLifetime = 30;
    public float inertia = 0.1f;
    public float sedimentCapacityFactor = 4.0f;
    public float minSlope = 0.01f;
    public float erosionSpeed = 0.3f;
    public float depositionSpeed = 0.3f;
    public float evaporationSpeed = 0.01f;
    public float gravity = 4.0f;


    private Terrain terrain;

    void Start()
    {
        terrain = GetComponent<Terrain>();
        GerarNovoTerreno();
    }

    void Update()
    {
        // Vazio
    }

    [ContextMenu("Gerar Novo Terreno")]
    public void GerarNovoTerreno()
    {
        if (terrain == null) terrain = GetComponent<Terrain>();

        if (terrain == null)
        {
            Debug.LogError("Erro Crítico: O script PerlinNoise precisa estar anexado no mesmo objeto que tem o componente 'Terrain'.");
            return;
        }

        offsetX = Random.Range(0f, 9999f);
        offsetY = Random.Range(0f, 9999f);
        rockOffset = Random.Range(0f, 9999f);

        Debug.Log("Gerando novo terreno... (Isso pode ser lento se a erosão estiver ativada)");
        System.DateTime startTime = System.DateTime.Now;
        
        terrain.terrainData = GenerateTerrain(terrain.terrainData);
        GenerateTrees(terrain);

        float timeTaken = (float)(System.DateTime.Now - startTime).TotalSeconds;
        Debug.Log($"Terreno gerado em {timeTaken:F2} segundos.");
    }

    TerrainData GenerateTerrain(TerrainData terrainData)
    {
        if (terrainData == null)
        {
            terrainData = new TerrainData();
        }

        terrainData.heightmapResolution = width + 1;
        terrainData.size = new Vector3(width, depth, height);

        float[,] heights = GenerateHeights();

        if (enableErosion)
        {
            ErodeTerrain(heights);
        }

        terrainData.SetHeights(0, 0, heights);
        terrainData.SetAlphamaps(0, 0, GenerateAlphamap(terrainData));
        return terrainData;
    }

    float[,] GenerateHeights()
    {
        float[,] heights = new float[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                heights[x, y] = CalculateHeights(x, y);
            }
        }
        return heights;
    }

    float CalculateHeights(int x, int y)
    {
        float totalHeight = 0;
        float amplitude = 1;
        float frequency = 1;
        float maxValue = 0;

        for (int i = 0; i < octaves; i++)
        {
            float xCoord = (float)x / width * scale * frequency + offsetX;
            float yCoord = (float)y / height * scale * frequency + offsetY;

            float perlinValue = Mathf.PerlinNoise(xCoord, yCoord) * 2 - 1;
            totalHeight += perlinValue * amplitude;

            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= 2;
        }

        return (totalHeight + maxValue) / (maxValue * 2);
    }

    #region Erosão Hidráulica
    // ... (Código da Erosão está aqui, sem alterações) ...
    void ErodeTerrain(float[,] heights)
    {
        for (int i = 0; i < erosionIterations; i++)
        {
            float posX = Random.Range(1.0f, width - 2.0f);
            float posY = Random.Range(1.0f, height - 2.0f);

            float dirX = 0, dirY = 0;
            float speed = 1;
            float water = 1;
            float sediment = 0;

            for (int lifetime = 0; lifetime < maxDropletLifetime; lifetime++)
            {
                int nodeX = (int)posX;
                int nodeY = (int)posY;
                float currentHeight = GetHeight(heights, posX, posY);

                float gradX, gradY;
                CalculateGradient(heights, posX, posY, out gradX, out gradY);

                dirX = (dirX * inertia) - (gradX * (1 - inertia));
                dirY = (dirY * inertia) - (gradY * (1 - inertia));

                float dirLen = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                if (dirLen < 0.0001f)
                {
                    dirX = Random.Range(-1f, 1f);
                    dirY = Random.Range(-1f, 1f);
                    dirLen = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                }
                dirX /= dirLen;
                dirY /= dirLen;

                posX += dirX;
                posY += dirY;

                if (posX < 1 || posX >= width - 2 || posY < 1 || posY >= height - 2)
                    break;

                float newHeight = GetHeight(heights, posX, posY);
                float heightDiff = currentHeight - newHeight;

                float sedimentCapacity = Mathf.Max(-heightDiff, minSlope) * speed * water * sedimentCapacityFactor;
                
                if (sediment > sedimentCapacity || heightDiff > 0)
                {
                    float amountToDeposit = (sediment - sedimentCapacity) * depositionSpeed;
                    if (heightDiff > 0)
                    {
                        amountToDeposit = Mathf.Min(sediment, heightDiff);
                    }
                    sediment -= amountToDeposit;
                    DepositSediment(heights, posX, posY, amountToDeposit);
                }
                else
                {
                    float amountToErode = Mathf.Min((sedimentCapacity - sediment), -heightDiff) * erosionSpeed;
                    
                    ErodeSediment(heights, posX, posY, amountToErode);
                    sediment += amountToErode;
                }

                speed = Mathf.Sqrt(speed * speed + heightDiff * gravity);
                
                water *= (1 - evaporationSpeed);
                if(water < 0.01f)
                    break;
            }
        }
    }
    void CalculateGradient(float[,] heights, float posX, float posY, out float gradX, out float gradY)
    {
        int x = (int)posX;
        int y = (int)posY;
        float fracX = posX - x;
        float fracY = posY - y;

        float h_tl = heights[x, y];
        float h_tr = heights[x + 1, y];
        float h_bl = heights[x, y + 1];
        float h_br = heights[x + 1, y + 1];

        gradX = (h_tr - h_tl) * (1 - fracY) + (h_br - h_bl) * fracY;
        gradY = (h_bl - h_tl) * (1 - fracX) + (h_br - h_tr) * fracX;
    }
    float GetHeight(float[,] heights, float posX, float posY)
    {
        int x = (int)posX;
        int y = (int)posY;
        float fracX = posX - x;
        float fracY = posY - y;

        float h_tl = heights[x, y];
        float h_tr = heights[x + 1, y];
        float h_bl = heights[x, y + 1];
        float h_br = heights[x + 1, y + 1];

        float h_top = Mathf.Lerp(h_tl, h_tr, fracX);
        float h_bottom = Mathf.Lerp(h_bl, h_br, fracX);
        return Mathf.Lerp(h_top, h_bottom, fracY);
    }
    void DepositSediment(float[,] heights, float posX, float posY, float amount)
    {
        int x = (int)posX;
        int y = (int)posY;
        float fracX = posX - x;
        float fracY = posY - y;

        heights[x, y] += amount * (1 - fracX) * (1 - fracY);
        heights[x + 1, y] += amount * fracX * (1 - fracY);
        heights[x, y + 1] += amount * (1 - fracX) * fracY;
        heights[x + 1, y + 1] += amount * fracX * fracY;
    }
    void ErodeSediment(float[,] heights, float posX, float posY, float amount)
    {
        int x = (int)posX;
        int y = (int)posY;
        float fracX = posX - x;
        float fracY = posY - y;
        
        float w_tl = (1 - fracX) * (1 - fracY);
        float w_tr = fracX * (1 - fracY);
        float w_bl = (1 - fracX) * fracY;
        float w_br = fracX * fracY;

        ErodeNode(heights, x, y, amount * w_tl);
        ErodeNode(heights, x + 1, y, amount * w_tr);
        ErodeNode(heights, x, y + 1, amount * w_bl);
        ErodeNode(heights, x + 1, y + 1, amount * w_br);
    }
    float ErodeNode(float[,] heights, int x, int y, float amount)
    {
        float currentHeight = heights[x, y];
        float newHeight = Mathf.Max(0, currentHeight - amount);
        float change = currentHeight - newHeight;
        heights[x, y] = newHeight;
        return change;
    }
    #endregion

    #region Texturização e Árvores

    float CalculateTextureValue(float normX, float normY, float scale, float offsetX, float offsetY)
    {
        float totalValue = 0;
        float amplitude = 1;
        float frequency = 1;
        float maxValue = 0;

        for (int i = 0; i < textureOctaves; i++)
        {
            float xCoord = normX * scale * frequency + offsetX;
            float yCoord = normY * scale * frequency + offsetY;

            totalValue += Mathf.PerlinNoise(xCoord, yCoord) * amplitude;

            maxValue += amplitude;
            amplitude *= texturePersistence;
            frequency *= 2;
        }

        return totalValue / maxValue;
    }

    // === FUNÇÃO ATUALIZADA ===
    float[,,] GenerateAlphamap(TerrainData terrainData)
    {
        if (terrainData == null)
        {
            Debug.LogError("GenerateAlphamap falhou: terrainData é nulo.");
            return new float[0, 0, 0];
        }

        // Verifica se temos PELO MENOS 2 camadas (Grama, Rocha)
        if (terrainData.alphamapLayers < 2)
        {
            Debug.LogWarning("O terreno precisa de pelo menos 2 Terrain Layers (Grama, Rocha) para este script funcionar.");
            return new float[terrainData.alphamapWidth, terrainData.alphamapHeight, terrainData.alphamapLayers];
        }

        // Verifica se temos 3 camadas (Grama, Rocha, Areia)
        bool hasSandLayer = terrainData.alphamapLayers > 2;
        if(hasSandLayer)
        {
            Debug.Log("Detectada Camada de Areia (Índice 2).");
        }


        float[,,] alphamap = new float[terrainData.alphamapWidth, terrainData.alphamapHeight, terrainData.alphamapLayers];

        for (int y = 0; y < terrainData.alphamapHeight; y++)
        {
            for (int x = 0; x < terrainData.alphamapWidth; x++)
            {
                float normX = (float)x / terrainData.alphamapWidth;
                float normY = (float)y / terrainData.alphamapHeight;

                float normalizedHeight = terrainData.GetInterpolatedHeight(normX, normY) / terrainData.size.y;
                float[] splatWeights = new float[terrainData.alphamapLayers];

                // --- LÓGICA DE TEXTURA ATUALIZADA ---

                // Parâmetros de altura (você pode torná-los públicos se quiser)
                float sandMaxHeight = 0.2f;  // Areia domina abaixo disso
                float sandBlend = 0.05f;      // Transição suave da areia para a grama
                float rockMinHeight = 0.4f;   // Rocha começa a aparecer aqui
                float rockMaxHeight = 0.7f;   // Rocha domina acima disso

                // Ruído para a rocha (como já existia)
                float rockPerlin = CalculateTextureValue(normX, normY, textureScale, rockOffset, rockOffset);

                // --- Cálculo dos Pesos ---

                // Camada 1: ROCHA (Índice 1)
                float rockWeight = Mathf.InverseLerp(rockMinHeight, rockMaxHeight, normalizedHeight) * rockPerlin * rockDensity;

                // Valores para Areia e Grama
                float sandWeight = 0;
                float grassWeight = 0;

                if (hasSandLayer)
                {
                    // Camada 2: AREIA (Índice 2)
                    // Começa em 1.0 (altura 0) e desaparece suavemente
                    sandWeight = Mathf.InverseLerp(sandMaxHeight + sandBlend, sandMaxHeight - sandBlend, normalizedHeight);
                
                    // Camada 0: GRAMA (Índice 0)
                    // A grama preenche o "meio".
                    // Começa em 1.0 e desaparece na areia E na rocha
                    float grassBlendToSand = Mathf.InverseLerp(sandMaxHeight - sandBlend, sandMaxHeight + sandBlend, normalizedHeight);
                    float grassBlendToRock = Mathf.InverseLerp(rockMinHeight + 0.1f, rockMinHeight - 0.1f, normalizedHeight);
                    grassWeight = grassBlendToSand * grassBlendToRock; // Combina as duas transições
                }
                else
                {
                    // Lógica antiga (sem areia)
                    grassWeight = 1.0f - rockWeight;
                }

                // --- Atribuição e Normalização ---
                splatWeights[0] = grassWeight;
                splatWeights[1] = rockWeight;
                if (hasSandLayer)
                {
                    splatWeights[2] = sandWeight;
                }

                // Normaliza os pesos (IMPORTANTE!)
                float totalWeight = 0;
                for (int i = 0; i < splatWeights.Length; i++)
                {
                    totalWeight += splatWeights[i];
                }

                if (totalWeight > 0.0001f)
                {
                    for (int i = 0; i < splatWeights.Length; i++)
                    {
                        alphamap[y, x, i] = splatWeights[i] / totalWeight;
                    }
                }
            }
        }
        return alphamap;
    }

    void GenerateTrees(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
        {
             Debug.LogError("GenerateTrees falhou: terrain ou terrain.terrainData é nulo.");
            return;
        }

        List<TreeInstance> treeInstances = new List<TreeInstance>();
        float treePerlinOffset = Random.Range(0f, 9999f);

        for (int i = 0; i < treeCount; i++)
        {
            float x = Random.Range(0f, 1f);
            float z = Random.Range(0f, 1f);

            float normalizedHeight = terrain.terrainData.GetInterpolatedHeight(x, z) / terrain.terrainData.size.y;
            Vector3 normal = terrain.terrainData.GetInterpolatedNormal(x, z);
            float slope = Vector3.Angle(Vector3.up, normal);

            float perlinValue = Mathf.PerlinNoise(x * treePerlinScale + treePerlinOffset, z * treePerlinScale + treePerlinOffset);

            if (perlinValue > treePerlinThreshold && slope < treePlacementSlopeLimit && normalizedHeight > treePlacementHeight)
            {
                TreeInstance treeInstance = new TreeInstance();
                treeInstance.position = new Vector3(x, normalizedHeight, z);
                treeInstance.prototypeIndex = 0;
                treeInstance.widthScale = 1;
                treeInstance.heightScale = 1;
                treeInstance.rotation = Random.Range(0, 360);
                treeInstance.color = Color.white;
                treeInstance.lightmapColor = Color.white;

                treeInstances.Add(treeInstance);
            }
        }

        terrain.terrainData.SetTreeInstances(treeInstances.ToArray(), true);
    }

    #endregion
}

