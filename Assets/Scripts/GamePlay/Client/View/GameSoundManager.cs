using UnityEngine;
using Mahjong.Model;
namespace GamePlay.Client.View
{
    public class GameSoundManager : MonoBehaviour
    {
		private const string SoundEffectsKey = "Audio.SoundEffectsEnabled";
		private const string MusicKey = "Audio.MusicEnabled";
        private static GameSoundManager instance;
        private AudioSource source;
		private AudioSource musicSource;
		private void Awake()
		{
			if (instance != null && instance != this) { Destroy(gameObject); return; }
			instance = this;
			DontDestroyOnLoad(gameObject);
			source = gameObject.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 0f;
			source.volume = .65f;
			// 某些单机场景没有摄像机 AudioListener，声音会执行但听不到。
			if (FindObjectOfType<AudioListener>() == null)
				gameObject.AddComponent<AudioListener>();
		}
        private static GameSoundManager I { get { if(instance==null){var g=new GameObject("GameSoundManager");instance=g.AddComponent<GameSoundManager>();DontDestroyOnLoad(g);} return instance;} }
		public static bool SoundEffectsEnabled { get { return PlayerPrefs.GetInt(SoundEffectsKey, 1) != 0; } }
		public static bool MusicEnabled { get { return PlayerPrefs.GetInt(MusicKey, 1) != 0; } }
		public static void SetSoundEffectsEnabled(bool enabled)
		{
			PlayerPrefs.SetInt(SoundEffectsKey, enabled ? 1 : 0);
			PlayerPrefs.Save();
			if (!enabled && instance != null && instance.source != null) instance.source.Stop();
			else if (enabled) I.Play("ui_click", 620, 620, .08f);
		}
		public static void SetMusicEnabled(bool enabled)
		{
			PlayerPrefs.SetInt(MusicKey, enabled ? 1 : 0);
			PlayerPrefs.Save();
			if (!enabled && instance != null && instance.musicSource != null) instance.musicSource.Stop();
			else if (enabled) PlayMusic();
		}
		public static void Draw(){ I.Play("deal", 520, 520, .07f); }
		public static void Meld(){ I.Play("peng", 420, 620, .16f); }
		public static void Win(){ I.Play("win", 520, 880, .28f); }
		public static void Discard(Tile tile, int playerIndex)
		{
			I.Play("tray", 520, 520, .07f);
			I.Play(TileClipName(tile, UseMaleVoice(playerIndex)), 520, 520, .12f);
		}
		public static void Chow(int playerIndex){ I.Play(VoiceName("chi", playerIndex), 420, 620, .16f); }
		public static void Pong(int playerIndex){ I.Play(VoiceName("peng", playerIndex), 420, 620, .16f); }
		// 本项目的点笑、回头笑和闷笑都属于杠类操作。
		public static void KongOrSmile(int playerIndex){ I.Play(VoiceName("gang", playerIndex), 380, 680, .2f); }
		public static void Hu(int playerIndex){ I.Play(VoiceName("hu", playerIndex), 520, 880, .28f); }
		public static void Play(string clipName){ I.Play(clipName, 520, 520, .07f); }
		public static void PlayMusic(string clipName = "playingInGame")
		{
			if (!MusicEnabled) return;
			AudioClip clip = Resources.Load<AudioClip>("Sound/" + clipName);
			if (clip == null) return;
			if (I.musicSource == null)
			{
				I.musicSource = I.gameObject.AddComponent<AudioSource>();
				I.musicSource.loop = true;
				I.musicSource.spatialBlend = 0f;
				I.musicSource.volume = .45f;
			}
			if (I.musicSource.clip == clip && I.musicSource.isPlaying) return;
			I.musicSource.clip = clip;
			I.musicSource.Play();
		}
		private void Play(string clipName, float fallbackStart, float fallbackEnd, float fallbackDuration)
		{
			if (!SoundEffectsEnabled) return;
			AudioClip clip = Resources.Load<AudioClip>("Sound/" + clipName);
			if (clip != null) source.PlayOneShot(clip);
			else Tone(fallbackStart, fallbackEnd, fallbackDuration);
		}
		private static bool UseMaleVoice(int playerIndex){ return playerIndex % 2 == 1; }
		private static string VoiceName(string name, int playerIndex)
		{
			return UseMaleVoice(playerIndex) ? "g_" + name : name;
		}
		private static string TileClipName(Tile tile, bool male)
		{
			string name;
			switch (tile.Suit)
			{
				case Suit.M: name = tile.Rank + "wan"; break;
				case Suit.P: name = tile.Rank + "tong"; break;
				case Suit.S: name = tile.Rank + "tiao"; break;
				case Suit.Z:
					switch (tile.Rank)
					{
						case 1: name = "dongfeng"; break;
						case 2: name = "nanfeng"; break;
						case 3: name = "xifeng"; break;
						case 4: name = "beifeng"; break;
						case 5: name = "bai"; break;
						case 6: name = "fa"; break;
						default: name = "zhong"; break;
					}
					break;
				default: name = "select"; break;
			}
			return male ? "g_" + name : name;
		}
        private void Tone(float a,float b,float d){const int r=44100;int n=Mathf.Max(1,Mathf.RoundToInt(r*d));var c=AudioClip.Create("mahjong_sfx",n,1,r,false);var x=new float[n];for(int i=0;i<n;i++){float t=i/(float)n; x[i]=Mathf.Sin(2*Mathf.PI*Mathf.Lerp(a,b,t)*i/r)*Mathf.Sin(Mathf.PI*t)*.35f;}c.SetData(x,0);source.PlayOneShot(c);Destroy(c,d+.1f);}
    }
}
