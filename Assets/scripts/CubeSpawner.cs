﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Pison
{
    public class CubeSpawner : MonoBehaviour
    {
        public enum BeatMode
        {
            Reading,
            Writing
        }

        public GameObject cubePrefab;

        public List<Color> bucketColors = new List<Color>();
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public AudioSource audioSource;
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI playText;

        public float pulseCooldown = 0.1f;
        public float columnLength = 1.0f;
        public float columnBaseColorMultiplier = 0.2f;

        public string assetRelativeAudioPath;
        public string assetRelativeBeatScriptPath;
        public int bpm = 105;
        public float beatReadTimeOffset = 0.0f;
        public BeatMode mode;

        private int cubeScore_ = 0;
        private int nextSpawnBucket = 0;
        private PisonBeatReader beatReader_;
        private PisonBeatWriter beatWriter_;
        private int[] recordedBuckets_;
        private List<Material> materials_ = new List<Material>();
        private List<Color> materialPulseColor_ = new List<Color>();

        async void Awake()
        {
            recordedBuckets_ = new int[bucketColors.Count];
            for (int i = 0; i < bucketColors.Count; i++)
            {
                recordedBuckets_[i] = 0;
            }

            var mesh = new Mesh();

            mesh.subMeshCount = bucketColors.Count;
            Vector3[] vertices = new Vector3[4 * bucketColors.Count];

            float dim = (1.0f / bucketColors.Count);
            for (int i = 0; i < bucketColors.Count; i++)
            {
                Vector3 bucketLeftTopCorner =
                    new
                        Vector3(-0.5f + i * dim, 1.0f, 2.0f);

                vertices[4 * i + 0] = bucketLeftTopCorner;
                vertices[4 * i + 1] = bucketLeftTopCorner + new Vector3(dim, 0.0f, 0.0f);
                vertices[4 * i + 2] = bucketLeftTopCorner + new Vector3(dim, -columnLength, 0.0f);
                vertices[4 * i + 3] = bucketLeftTopCorner + new Vector3(0.0f, -columnLength, 0.0f);
            }

            mesh.vertices = vertices;
            for (int i = 0; i < bucketColors.Count; i++)
            {
                int[] indices = new int[6]
                {
                    0 + 4 * i,
                    1 + 4 * i,
                    3 + 4 * i,
                    3 + 4 * i,
                    1 + 4 * i,
                    2 + 4 * i
                };
                mesh.SetTriangles(indices, i);
                var material = meshRenderer.materials[i];
                material.color = columnBaseColorMultiplier * bucketColors[i];
                materials_.Add(material);
                materialPulseColor_.Add(Color.black);
            }

            meshFilter.sharedMesh = mesh;

            audioSource.clip = null;
            var audioClip = await LoadMusic(assetRelativeAudioPath);
            audioSource.clip = audioClip;
            audioSource.Play();

            cubeScore_ = 0;
            if (mode == BeatMode.Reading)
            {
                var beatScriptPath = System.IO.Path.Combine(Application.dataPath, assetRelativeBeatScriptPath);
                var beatScript = System.IO.File.ReadAllText(beatScriptPath.ToString());
                beatReader_ = new PisonBeatReader(beatScript, bucketColors.Count, bpm);
                scoreText.text = $"Score: 0";
            }
            else
            {
                beatWriter_ = new PisonBeatWriter(bucketColors.Count, bpm);
                beatWriter_.StartRecording();
                scoreText.text = $"Recording Beats";
            }

            playText.text = "0 :Play";
        }

        async Task<AudioClip> LoadMusic(string inAssetAudioPath)
        {
            var audioPath = System.IO.Path.Combine(Application.dataPath, inAssetAudioPath);
            var www = await new WWW(audioPath.ToString());
            if (!string.IsNullOrEmpty(www.error))
            {
                throw new Exception(www.error);
            }

            var audioClip = www.GetAudioClip(false);
            return audioClip;
        }

        void Update()
        {
            if (bucketColors.Count == 0)
            {
                return;
            }

            if (!audioSource.isPlaying)
            {
                return;
            }

            for (int i = 0; i < bucketColors.Count; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    recordedBuckets_[i] = 1;
                }
            }

            if (mode == BeatMode.Writing)
            {
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    WriteOutBeatScript();
                }
            }

            int[] buckets = new int[bucketColors.Count];
            if (mode == BeatMode.Reading)
            {
                BeatFrame frame = new BeatFrame();
                if (beatReader_.Update(audioSource.time + beatReadTimeOffset, out frame))
                {
                    buckets = frame.bucketBits;
                }
            }
            else
            {
                if (!beatWriter_.IsRecording())
                {
                    return;
                }

                if (beatWriter_.Update(audioSource.time, recordedBuckets_))
                {
                    recordedBuckets_.CopyTo(buckets, 0);
                    for (int i = 0; i < bucketColors.Count; i++)
                    {
                        recordedBuckets_[i] = 0;
                    }
                }
            }

            for (int i = 0;
                i < buckets.Length;
                i++)
            {
                if (buckets[i] > 0)
                {
                    var bucketColor = bucketColors[i];

                    Vector3 bucketHalfDimensions = new Vector3(transform.lossyScale.x / bucketColors.Count,
                        transform.lossyScale.y,
                        transform.localScale.z);

                    Vector3 bucketPos =
                        new
                            Vector3(
                                this.transform.position.x + -.5f * this.transform.lossyScale.x +
                                (.5f + i) * bucketHalfDimensions.x,
                                this.transform.position.y,
                                this.transform.position.z);

                    Quaternion rot = Quaternion.Euler(Random.Range(0.0f, 360.0f),
                        Random.Range(0.0f, 360.0f),
                        Random.Range(0.0f, 360.0f));

                    var cube = GameObject.Instantiate(cubePrefab, bucketPos, rot);
                    var cubePawn = cube.GetComponent<CubePawn>();
                    cubePawn.UpdateColor(bucketColor);
                    cubePawn.spawner = this;
                    cubePawn.bucketColumn = i;
                    PulseColumn(i, bucketColor);
                }

                playText.text = $"{audioSource.time:F2} :Time";
            }

            for (int i = 0;
                i < materialPulseColor_.Count;
                i++)
            {
                float pulseAmount = materialPulseColor_[i].grayscale;
                if (pulseAmount > 0.0f)
                {
                    materials_[i].SetColor("_Color",
                            columnBaseColorMultiplier * bucketColors[i] + materialPulseColor_[i])
                        ;
                    materialPulseColor_[i] = Color.Lerp(materialPulseColor_[i], Color.black, pulseCooldown);
                }
            }

            if (!audioSource.isPlaying && mode == BeatMode.Writing)
            {
                WriteOutBeatScript();
            }
        }

        public void PulseColumn(int inColumn, Color inPulseColor)
        {
            materialPulseColor_[inColumn] = inPulseColor;
        }

        public void AddToScore(int inAmount)
        {
            if (mode == BeatMode.Reading)
            {
                cubeScore_ += inAmount;
                scoreText.text = $"Score: {cubeScore_}";
            }
        }

        protected void WriteOutBeatScript()
        {
            if (beatWriter_.IsRecording())
            {
                var path = Path.Combine(Application.dataPath, assetRelativeBeatScriptPath);
                beatWriter_.WriteBeatsTo(path.ToString());
                beatWriter_.StopRecording();
            }
        }

        void OnDrawGizmos()
        {
            float halfDimX = this.transform.lossyScale.x / bucketColors.Count;

            Vector3 pos = new Vector3(-this.transform.lossyScale.x * .5f + .5f * halfDimX,
                this.transform.position.y,
                this.transform.position.z);

            Vector3 scale = new Vector3(halfDimX, this.transform.lossyScale.y, this.transform.lossyScale.z);
            foreach (var bucketColor in bucketColors)
            {
                Gizmos.color = bucketColor;
                Gizmos.DrawWireCube(pos, scale);
                pos.x += halfDimX;
            }
        }
    }
}