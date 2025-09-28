using System;
using Seb.Fluid2D.Rendering;
using Seb.Helpers;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Seb.Fluid2D.Simulation
{
	public class FluidSim2D : MonoBehaviour
	{
		public event System.Action SimulationStepCompleted;

		[Header("Simulation Settings")]
		public float timeScale = 1;
		public float maxTimestepFPS = 60; // if time-step dips lower than this fps, simulation will run slower (set to 0 to disable)
		public int iterationsPerFrame;
		public float gravity;
		[Range(0, 1)] public float collisionDamping = 0.95f;
		public float smoothingRadius = 2;
		public float targetDensity;
		public float pressureMultiplier;
		public float nearPressureMultiplier;
		public float viscosityStrength;
		public Vector2 boundsSize;
		public Vector2 obstacleSize;
		public Vector2 obstacleCentre;

		[Header("Interaction Settings")]
		public float interactionRadius;

		public float interactionStrength;

		[Header("References")]
		public ComputeShader compute;

		public Spawner2D spawner2D;

		// Buffers
		public struct Particle
		{
			public Vector2 position;
			public Vector2 predictedPositions;
			public Vector2 velocity;
		}
		public ComputeBuffer Particles { get; private set; }
		public ComputeBuffer DensityBuffer { get; private set; }

		public struct SortTarget
		{
			public Vector2 position;
			public Vector2 predictedPositions;
			public Vector2 velocity;
		}
		
		public ComputeBuffer SortTargets { get; private set; }

		
		SpatialHash spatialHash;

		// Kernel IDs
		const int externalForcesKernel = 0;
		const int spatialHashKernel = 1;
		const int reorderKernel = 2;
		const int copybackKernel = 3;
		const int densityKernel = 4;
		const int pressureKernel = 5;
		const int viscosityKernel = 6;
		const int updatePositionKernel = 7;

		// State
		private bool _isPaused;
		private Spawner2D.ParticleSpawnData _spawnData;
		private bool _pauseNextFrame;

		public int NumParticles { get; private set; }
		private int BufferSize { get; set; }


		void Start()
		{
			Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel");

			Init();
		}

		void Init()
		{
			float deltaTime = 1 / 60f;
			Time.fixedDeltaTime = deltaTime;

			// Get spawn data.
			_spawnData = spawner2D.GetSpawnData();
			
			// Init compute shader buffers etc.
			InitComputeOnStart(_spawnData);
		}
		
		void Update()
		{
			HandleInput();
			
			if (spawnPending)
			{
				SpawnParticleAtMouse();
				spawnPending = false;
				_isPaused = false;
			}

			if (!_isPaused)
			{
				float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity;
				float dt = Mathf.Min((Time.deltaTime + pendingDeltaTime) * timeScale, maxDeltaTime);
				pendingDeltaTime = 0f;
				RunSimulationFrame(dt);
			}

			if (_pauseNextFrame)
			{
				_isPaused = true;
				_pauseNextFrame = false;
			}
		}

		/// <summary>
		/// Set number of particles, resizing buffers if necessary.
		/// Note: Buffer length is always a power of two, so may be larger than the number of particles.
		/// If the number of particles exceeds the current buffer length, the buffer length is increased to the next power of two.
		/// </summary>
		/// <param name="numParticles"></param>
		/// <returns> True if buffers were resized, false otherwise. </returns>
		private bool SetNumParticles(int numParticles)
		{
			if (NumParticles == numParticles)
				return false;

			bool resized = false;
			
			NumParticles = numParticles;
			
			// Ensure buffer length is a power of two and can fit all particles.
			if (NumParticles > BufferSize)
			{
				BufferSize = NumParticles;//Mathf.NextPowerOfTwo(NumParticles) * 2;
				Debug.Log($"Set buffer size to {BufferSize} for {NumParticles} particles");
				
				// Create buffers when size changes.
				CreateBuffer(BufferSize);
				InitCompute();
				resized = true;
			}
			
			// Spatial hash mast be re-initialised if number of particles changes.
			ReleaseSpatialHash();
			spatialHash = new SpatialHash(NumParticles);
			ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel);
			ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
			ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
			
			compute.SetInt("numParticles", NumParticles);
			
			return resized;
		}

		/// <summary>
		/// Initialise compute shader, buffers, and spatial hash.
		/// </summary>
		/// <param name="spawnData"></param>
		private void InitComputeOnStart(Spawner2D.ParticleSpawnData spawnData)
		{
			// set number of particles and create buffers.
			SetNumParticles(spawnData.positions.Length);
			
			// Set buffer data.
			SetInitialBufferData(spawnData);
		}

		private void CreateBuffer(int length)
		{
			// Release existing buffers when changing size.
			ReleaseBuffer();
			
			DensityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(length);
			Particles = ComputeHelper.CreateStructuredBuffer<Particle>(length);
			SortTargets = ComputeHelper.CreateStructuredBuffer<SortTarget>(length);
		}
		
		private void InitCompute()
		{
			// Init compute
			ComputeHelper.SetBuffer(compute, Particles, "Particles", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, DensityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);
			
			ComputeHelper.SetBuffer(compute, SortTargets, "SortTargets", reorderKernel, copybackKernel);
		}
		
		private void ReleaseBuffer()
		{
			ComputeHelper.Release(Particles, DensityBuffer, SortTargets);
		}
		
		private void ReleaseSpatialHash()
		{
			spatialHash?.Release();
		}

		void RunSimulationFrame(float frameTime)
		{
			float timeStep = frameTime / iterationsPerFrame;

			UpdateSettings(timeStep);

			for (int i = 0; i < iterationsPerFrame; i++)
			{
				RunSimulationStep();
				SimulationStepCompleted?.Invoke();
			}
		}

		void RunSimulationStep()
		{
			ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: externalForcesKernel);

			RunSpatial();

			ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: densityKernel);
			ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: pressureKernel);
			ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: viscosityKernel);
			ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: updatePositionKernel);
		}

		void RunSpatial()
		{
			ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: spatialHashKernel);
			spatialHash.Run();

			ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: reorderKernel);
			ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: copybackKernel);
		}

		void UpdateSettings(float deltaTime)
		{
			compute.SetFloat("deltaTime", deltaTime);
			compute.SetFloat("gravity", gravity);
			compute.SetFloat("collisionDamping", collisionDamping);
			compute.SetFloat("smoothingRadius", smoothingRadius);
			compute.SetFloat("targetDensity", targetDensity);
			compute.SetFloat("pressureMultiplier", pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength", viscosityStrength);
			compute.SetVector("boundsSize", boundsSize);
			compute.SetVector("obstacleSize", obstacleSize);
			compute.SetVector("obstacleCentre", obstacleCentre);

			compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
			compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
			compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
			compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
			compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));
		}

		void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
		{
			Particle[] particles = new Particle[spawnData.positions.Length];
			for (int i = 0; i < particles.Length; i++)
			{
				particles[i] = new Particle()
				{
					position = spawnData.positions[i],
					predictedPositions = spawnData.positions[i],
					velocity = spawnData.velocities[i],
				};
			}
			Particles.SetData(particles);
		}

		void HandleInput()
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				_isPaused = !_isPaused;
			}

			if (Input.GetKeyDown(KeyCode.RightArrow))
			{
				_isPaused = false;
				_pauseNextFrame = true;
			}
			
			// Mouse interaction settings:
			Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
			bool isPullInteraction = Input.GetMouseButton(0);
			bool isPushInteraction = Input.GetMouseButton(1);
			float currInteractStrength = 0;
			if (isPushInteraction || isPullInteraction)
				currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;

			compute.SetVector("interactionInputPoint", mousePos);
			compute.SetFloat("interactionInputStrength", currInteractStrength);
			compute.SetFloat("interactionInputRadius", interactionRadius);

			if (Input.GetKeyDown(KeyCode.R))
			{
				_isPaused = true;
				// Reset positions, the run single frame to get density etc (for debug purposes) and then reset positions again
				SetInitialBufferData(_spawnData);
				RunSimulationStep();
				SetInitialBufferData(_spawnData);
			}

			HandleInputSpawn();
		}

		void OnDestroy()
		{
			ReleaseBuffer();
			ReleaseSpatialHash();
		}

		void OnDrawGizmos()
		{
			Gizmos.color = new Color(0, 1, 0, 0.4f);
			Gizmos.DrawWireCube(Vector2.zero, boundsSize);
			Gizmos.DrawWireCube(obstacleCentre, obstacleSize);

			if (Application.isPlaying)
			{
				Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
				bool isPullInteraction = Input.GetMouseButton(0);
				bool isPushInteraction = Input.GetMouseButton(1);
				bool isInteracting = isPullInteraction || isPushInteraction;
				if (isInteracting)
				{
					Gizmos.color = isPullInteraction ? Color.green : Color.red;
					Gizmos.DrawWireSphere(mousePos, interactionRadius);
				}
			}
		}

		#region Spawn

		private bool isSpawning = false;
		private bool spawnPending = false;
		private float pendingDeltaTime = 0f;

		private void HandleInputSpawn()
		{
			// 目前添加粒子功能有bug。偶现在添加粒子时，所有粒子模拟会出问题，直到下一次添加粒子更新数据后又恢复。
			return;
			
			if (Input.GetKeyDown(KeyCode.S) && !_isPaused)
			{
				isSpawning = true;
				spawnPending = true;
				pendingDeltaTime = Time.deltaTime;
				_isPaused = true;
			}
			if (Input.GetKeyUp(KeyCode.S))
				isSpawning = false;

			if (isSpawning)
			{
				SpawnParticleAtMouse();
			}
		}

		private void SpawnParticleAtMouse()
		{
			Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
			AddParticle(mousePos, Vector2.zero);
		}
		
		// 新增粒子到 spawnData
		public void AddParticle(Vector2 position, Vector2 velocity)
		{
			Particle[] particles = new Particle[NumParticles];
			Particles.GetData(particles);

			bool resized = SetNumParticles(NumParticles + 1);

			if (resized)
			{
				Particle[] newParticles = new Particle[NumParticles];
				Array.Copy(particles, newParticles, particles.Length);
				newParticles[NumParticles - 1].position = position;
				newParticles[NumParticles - 1].predictedPositions = position;
				newParticles[NumParticles - 1].velocity = velocity;
				Particles.SetData(newParticles);
			}
			else
			{
				Particle newParticle = new Particle
				{
					position = position,
					predictedPositions = position,
					velocity = velocity
				};
				Particles.SetData(new[] { newParticle }, 0, NumParticles - 1, 1);
			}
		}
		#endregion
	}
}