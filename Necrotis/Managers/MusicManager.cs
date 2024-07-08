using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Necrotis.Managers;

public class MusicManager
{
    private static readonly List<Music> m_musics = new();

    static MusicManager()
    {
        Harmony harmony = new("org.bepinex.helpers.MusicManager");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ZoneSystem), nameof(ZoneSystem.SetupLocations)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(MusicManager), nameof(RegisterEnvironments))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(MusicMan), nameof(MusicMan.Awake)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(MusicManager), nameof(RegisterTunes))));

    }

    internal static void RegisterEnvironments()
    {
        if (!EnvMan.instance) return;

        foreach (Music? music in m_musics)
        {
            EnvSetup env = EnvMan.instance.GetEnv(music.m_clonedEnvironment);
            if (env == null) continue;
            EnvSetup? clone = env.Clone();
            clone.m_name = music.m_name;
            clone.m_musicDay = music.m_name;
            clone.m_musicEvening = music.m_name;
            clone.m_musicMorning = music.m_name;
            clone.m_musicNight = music.m_name;
            EnvMan.instance.m_environments.Add(clone);
        }
    }

    internal static void RegisterTunes(MusicMan __instance)
    {
        foreach (Music? music in m_musics)
        {
            __instance.m_music.Add(new MusicMan.NamedMusic()
            {
                m_name = music.m_name,
                m_clips = new []{music.m_clip},
                m_volume = music.m_volume,
                m_fadeInTime = music.m_fadeInTime,
                m_alwaysFadeout = music.m_alwaysFadeOut,
                m_loop = music.m_loop,
                m_resume = music.m_resume,
                m_ambientMusic = music.m_ambientMusic
            });
        }
    }
    
    public class Music
    {
        public string m_name;
        public AudioClip m_clip;
        public string m_clonedEnvironment = "Clear";
        public float m_volume = 1f;
        public float m_fadeInTime = 3f;
        public bool m_alwaysFadeOut = false;
        public bool m_loop = true;
        public bool m_resume = false;
        public bool m_ambientMusic = true;

        public Music(string name, string searchName, AssetBundle bundle)
        {
            m_name = searchName;
            m_clip = bundle.LoadAsset<AudioClip>(name);
            m_musics.Add(this);
        }

        public void SetEnvironmentCopy(string env) => m_clonedEnvironment = env;
    }
}