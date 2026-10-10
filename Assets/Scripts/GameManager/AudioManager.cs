using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;

[Serializable]
public class PhaseMusic
{
    [FormerlySerializedAs("gameMode")]
    public GamePhase phase;
    public AudioClip track;
}

// Presentation only: observes game state, never starts/ends rounds or changes players.
public class AudioManager : MonoBehaviour
{
    public static AudioManager _instance { get; private set; }

    [Header("Music")]
    [SerializeField] private List<PhaseMusic> musicTracks = new();
    [SerializeField] private AudioMixerGroup musicMixerGroup;
    [SerializeField, Min(0f)] private float crossfadeDuration = 1.5f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;

    [Header("Optional Round Cues")]
    [SerializeField] private AudioMixerGroup cuesMixerGroup;
    [SerializeField, Range(0f, 1f)] private float cuesVolume = 1f;
    [SerializeField] private AudioClip roundStarted;
    [SerializeField] private AudioClip hiderCaught;
    [SerializeField] private AudioClip hidersWon;
    [SerializeField] private AudioClip seekerWon;

    private AudioSource sourceA;
    private AudioSource sourceB;
    private AudioSource cuesSource;

    private GameManager subscribedGameManager;
    private Coroutine transitionRoutine;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        sourceA = CreateSource(musicMixerGroup, true);
        sourceB = CreateSource(musicMixerGroup, true);
        cuesSource = CreateSource(cuesMixerGroup, false);
        cuesSource.volume = 1f;
    }

    private void OnEnable()
    {
        if (_instance != this)
        {
            return;
        }

        GameManager.InstanceChanged += SubscribeToGameManager;
        SubscribeToGameManager(GameManager._instance);
    }

    private void OnDisable()
    {
        GameManager.InstanceChanged -= SubscribeToGameManager;
        UnsubscribeFromGameManager();

        StopCurrentTransition();

        if (sourceA != null)
        {
            StopAndClear(sourceA);
        }

        if (sourceB != null)
        {
            StopAndClear(sourceB);
        }

        if (cuesSource != null)
        {
            StopAndClear(cuesSource);
        }
    }

    private void OnDestroy()
    {
        if (_instance != this)
        {
            return;
        }

        _instance = null;

        if (sourceA != null)
        {
            Destroy(sourceA);
        }

        if (sourceB != null)
        {
            Destroy(sourceB);
        }

        if (cuesSource != null)
        {
            Destroy(cuesSource);
        }
    }

    private AudioSource CreateSource(AudioMixerGroup mixer, bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.outputAudioMixerGroup = mixer;

        return source;
    }

    private void SubscribeToGameManager(GameManager gameManager)
    {
        UnsubscribeFromGameManager();
        subscribedGameManager = gameManager;

        if (gameManager == null)
        {
            HandlePhaseChanged(GamePhase.Lobby, true);
            return;
        }

        gameManager.PhaseChanged += HandlePhaseChanged;
        gameManager.PostStart += HandleRoundStarted;
        gameManager.HiderCaught += HandleHiderCaught;
        gameManager.PostEnd += HandleRoundEnded;

        // Late subscribers get current music, but don't replay earlier round cues.
        HandlePhaseChanged(gameManager.CurrentPhase);
    }

    private void UnsubscribeFromGameManager()
    {
        // ReferenceEquals also permits cleanup after Unity has destroyed the component.
        if (ReferenceEquals(subscribedGameManager, null))
        {
            return;
        }

        subscribedGameManager.PhaseChanged -= HandlePhaseChanged;
        subscribedGameManager.PostStart -= HandleRoundStarted;
        subscribedGameManager.HiderCaught -= HandleHiderCaught;
        subscribedGameManager.PostEnd -= HandleRoundEnded;

        subscribedGameManager = null;
    }

    private void HandleRoundStarted()
    {
        PlayCue(roundStarted);
    }

    private void HandleHiderCaught(Player player)
    {
        PlayCue(hiderCaught);
    }

    private void HandleRoundEnded(RoundResult result)
    {
        if (result == RoundResult.HidersWin)
        {
            PlayCue(hidersWon);
        }
        else if (result == RoundResult.SeekerWins)
        {
            PlayCue(seekerWon);
        }
    }

    private void PlayCue(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        cuesSource.volume = 1f;
        cuesSource.PlayOneShot(clip, cuesVolume);
    }

    private void HandlePhaseChanged(GamePhase phase)
    {
        HandlePhaseChanged(phase, false);
    }

    private void HandlePhaseChanged(GamePhase phase, bool silence)
    {
        AudioClip track = null;

        if (!silence)
        {
            foreach (PhaseMusic entry in musicTracks)
            {
                if (entry != null && entry.phase == phase)
                {
                    track = entry.track;
                    break;
                }
            }
        }

        StopCurrentTransition();

        AudioSource target = null;

        if (track != null)
        {
            // Reuse an already playing track when reversing an interrupted crossfade.
            if (sourceA.isPlaying && sourceA.clip == track)
            {
                target = sourceA;
            }
            else if (sourceB.isPlaying && sourceB.clip == track)
            {
                target = sourceB;
            }
            else
            {
                target = sourceA.volume <= sourceB.volume ? sourceA : sourceB;
                StopAndClear(target);
                target.clip = track;
                target.Play();
            }
        }

        // An unassigned track means silence; missing optional audio is not a gameplay error.
        transitionRoutine = StartCoroutine(FadeTo(target));
    }

    private void StopCurrentTransition()
    {
        if (transitionRoutine == null)
        {
            return;
        }

        StopCoroutine(transitionRoutine);
        transitionRoutine = null;
    }

    private IEnumerator FadeTo(AudioSource target)
    {
        float startA = sourceA.volume;
        float startB = sourceB.volume;

        float endA = target == sourceA ? musicVolume : 0f;
        float endB = target == sourceB ? musicVolume : 0f;

        float duration = crossfadeDuration;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            sourceA.volume = Mathf.Lerp(startA, endA, progress);
            sourceB.volume = Mathf.Lerp(startB, endB, progress);

            yield return null;
        }

        sourceA.volume = endA;
        sourceB.volume = endB;

        if (target != sourceA)
        {
            StopAndClear(sourceA);
        }

        if (target != sourceB)
        {
            StopAndClear(sourceB);
        }

        transitionRoutine = null;
    }

    private static void StopAndClear(AudioSource source)
    {
        source.Stop();
        source.clip = null;
        source.volume = 0f;
    }
}
