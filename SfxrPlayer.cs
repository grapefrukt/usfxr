using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
using Debug = UnityEngine.Debug;
#endif

namespace usfxr {
	
	/// <summary>
	/// This is the script responsible for providing rendered audio to the engine, it also handles caching
	/// </summary>
	
	[RequireComponent(typeof(AudioSource))]
	public class SfxrPlayer : MonoBehaviour {
		class ClipTimeTuple {
			public AudioClip clip;
			public long      time;
		}

		static readonly Dictionary<SfxrParams, ClipTimeTuple> cache = new Dictionary<SfxrParams, ClipTimeTuple>();

		static SfxrPlayer    instance;
		static SfxrRenderer  sfxrRenderer;
		static AudioSource[] sources;
		static int           sourceIndex;

		[Header("A higher polyphony means you can play more sound effects simultaneously.")]
		[Range(1, 16)]
		public int polyphony = 1;

		const int MaxCacheSize = 32;

		void Start() {
			cache.Clear();
			UpdateSources();
		}

		/// <summary>
		/// Call this from any MonoBehaviour to pre-cache all your sfx
		/// </summary>
		/// <param name="behaviour">Any of your games MonoBehaviours</param>
		public static void PreCache(MonoBehaviour behaviour) {
			var monobehaviourCount = 0;
			var fieldCount         = 0;
			
			var s = new Stopwatch();
			s.Start();

			foreach (var type in Assembly.GetAssembly(behaviour.GetType()).GetTypes()) {
				monobehaviourCount++;
				if (!type.IsClass || type.IsAbstract || !type.IsSubclassOf(typeof(MonoBehaviour))) continue;

				var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
				var objects = FindObjectsOfType(type);
				
				foreach (var obj in objects) {
					monobehaviourCount++;
					foreach (var field in fields) {
						if (field.FieldType != typeof(SfxrParams)) continue;
						CacheGet((SfxrParams) field.GetValue(obj));
						fieldCount++;
					}
				}
			}
			
			Debug.Log($"Pre cached {fieldCount} sfx found across {monobehaviourCount} components in {s.Elapsed.TotalMilliseconds:F1} ms");
		}

		#if UNITY_EDITOR
		void OnValidate() {
			UpdateSources();
			// make sure we have the correct amount of audio sources
			// this needs to be done later since unity gets grumpy if we add/remove components in OnValidate
			if (sources.Length != polyphony) EditorApplication.delayCall += PurgeAndAddSources;
		}

		void PurgeAndAddSources() {
			var numSources = sources.Length;

			while (numSources < polyphony) {
				gameObject.AddComponent<AudioSource>();
				numSources++;
			}
			
			while (numSources > polyphony) {
				DestroyImmediate(sources[numSources - 1]);
				numSources--;
			}
		}
		
		#endif

		/// <summary>
		/// Renders and plays the supplied SfxParams
		/// </summary>
		/// <param name="param">The sound effect parameters to use</param>
		/// <param name="asPreview">If set, the effect will always play on the first channel (this stops any previous preview that is still playing)</param>
		public static void Play(SfxrParams param, bool asPreview = false) {
			Purge();

			var entry = CacheGet(param);

			// sometimes it seems the audio clip will get lost despite the cache having a reference to it, so we may need to regenerate it
			if (entry.clip == null) {
				entry.clip = sfxrRenderer.GenerateClip();
			}

			PlayClip(entry.clip, asPreview);
		}

		/// <summary>
		/// Retrieves an AudioClip along with some other data if it's cached, otherwise it is generated 
		/// </summary>
		static ClipTimeTuple CacheGet(SfxrParams param) {
			if (cache.TryGetValue(param, out var entry)) return entry;
			
			// we can reuse the same renderer, but we need to update the params
			if (sfxrRenderer == null) sfxrRenderer = new SfxrRenderer();
			sfxrRenderer.param = param;

			entry = new ClipTimeTuple {
				clip = sfxrRenderer.GenerateClip(),
				time = GetTimestamp(),
			};
			cache.Add(param, entry);

			return entry;
		}

		static void PlayClip(AudioClip clip, bool asPreview) {
			UpdateInstance();

			if (sources == null) UpdateSources();
			if (sources.Length == 0) {
				Debug.LogError($"No {nameof(AudioSource)} found in on GameObject that has {nameof(SfxrPlayer)}. Add one!");
				return;
			}

			if (asPreview) {
				sources[0].PlayOneShot(clip);
			} else {
				sources[sourceIndex].PlayOneShot(clip);
				sourceIndex = (sourceIndex + 1) % sources.Length;
			}
		}

		static void UpdateInstance() {
			if (instance == null) instance = FindObjectOfType<SfxrPlayer>();
			if (instance == null) {
				Debug.LogError($"No {nameof(SfxrPlayer)} found in Scene. Add one!");
			}
		}

		static void UpdateSources() {
			UpdateInstance();
			sources = instance.GetComponents<AudioSource>();
		}

		/// <summary>
		/// Drops the oldest N sfx from the cache
		/// </summary>
		static void Purge() {
			if (cache.Count < MaxCacheSize) return;

			var now    = GetTimestamp();
			var maxAge = long.MinValue;
			var oldest = new SfxrParams();

			foreach (var entry in cache) {
				var age = now - entry.Value.time;
				if (age < maxAge) continue;
				maxAge = age;
				oldest = entry.Key;
			}

			cache.Remove(oldest);
		}

		static long GetTimestamp() {
			return DateTimeOffset.Now.ToUnixTimeSeconds();
		}
	}
}