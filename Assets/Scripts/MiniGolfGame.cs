using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// One-script playable 3D mini-golf prototype for Unity 6.
/// Attach this script to an empty GameObject and press Play.
/// It builds a curved green, bouncy curbs, golfer, ball, hole, camera, and HUD at runtime.
/// Updated version: directional aim arrow, 10x shot power, lifting flag, stylized yellow-suit golfer,
/// and a gold putter inspired by the user's reference image.
/// </summary>
public class MiniGolfGame : MonoBehaviour
{
    [Header("Course")]
    [Min(12f)] public float courseLength = 28f;
    [Min(3f)] public float courseWidth = 5.5f;
    [Range(0f, 9f)] public float curveAmount = 4.25f;
    [Range(12, 80)] public int curveSegments = 36;
    [Min(0.1f)] public float curbHeight = 0.45f;
    [Min(0.08f)] public float curbWidth = 0.28f;

    [Header("Ball")]
    [Range(0.15f, 0.5f)] public float ballRadius = 0.28f;
    [Min(0f)] public float minimumShotImpulse = 0f;
    [Min(2f)] public float maximumShotImpulse = 120f;    // 10x previous default
    [Min(0f)] public float ballLinearDamping = 0.42f;
    [Min(0f)] public float ballAngularDamping = 0.34f;
    [Range(0.01f, 1f)] public float stopSpeed = 0.42f;
    [Range(0.01f, 4f)] public float stopAngularSpeed = 0.8f;
    [Range(0.1f, 2f)] public float stopConfirmTime = 0.35f;

    [Header("Aiming / Power")]
    [Min(15f)] public float aimRotationSpeed = 95f;
    [Range(0.2f, 3f)] public float powerMeterSpeed = 1.1f;
    [Min(0.8f)] public float arrowBaseLength = 1.3f;
    [Min(0.2f)] public float arrowPowerLength = 2.2f;

    [Header("Camera")]
    public float aimingCameraDistance = 5.8f;
    public float aimingCameraHeight = 3.0f;
    public float aimingCameraRightOffset = 0.90f;
    public float movingCameraDistance = 6.8f;
    public float movingCameraHeight = 3.4f;
    [Range(1f, 20f)] public float cameraSmooth = 8f;
    [Range(0.02f, 1f)] public float mouseOrbitSensitivity = 0.18f;
    [Range(0.02f, 1f)] public float mousePitchSensitivity = 0.14f;
    [Range(5f, 80f)] public float maxCameraPitch = 55f;

    [Header("Hole")]
    [Range(0.25f, 0.7f)] public float holeRadius = 0.42f;
    [Range(0.5f, 8f)] public float holeCaptureMaxSpeed = 3.5f;

    [Header("Music")]
    [Tooltip("Import your licensed copy of Doperide by Saliva into Unity and assign it here.")]
    public AudioClip backgroundSong;
    [Range(0f, 1f)] public float musicVolume = 0.22f;

    [Header("Flag Lift")]
    [Min(0.5f)] public float flagTriggerDistance = 2.0f;
    [Min(0.2f)] public float flagLiftAmount = 1.15f;
    [Range(1f, 12f)] public float flagLiftSpeed = 4.5f;

    private enum GameState
    {
        Aiming,
        Swinging,
        BallMoving,
        Holed,
        OutOfStrokes
    }

    private GameState state;

    private GameObject runtimeRoot;
    private Transform playerRoot;
    private Transform clubPivot;
    private Rigidbody ballRb;
    private Camera gameCamera;

    private float cameraOrbitYaw;
    private float cameraOrbitPitch;
    private bool holePreview;

    private Transform flagPoleTransform;
    private Transform flagBannerTransform;
    private Vector3 flagPoleBaseLocalPosition;
    private Vector3 flagBannerBaseLocalPosition;
    private float flagLiftLerp;

    private Transform aimArrowRoot;
    private Transform aimArrowShaft;
    private Transform aimArrowHead;
    private Vector3 arrowTipWorld;

    private Vector3 ballStartPosition;
    private Vector3 holePosition;
    private Vector3 lastLie;
    private Vector3 followDirection = Vector3.forward;

    private float aimYaw;
    private bool charging;
    private float chargeClock;
    private float power01;
    private float stoppedTimer;
    private float shotStartTime;

    private int strokes;
    private float strokePopupTimer;

    private Material greenMaterial;
    private Material curbMaterial;
    private Material ballMaterial;
    private Material yellowSuitMaterial;
    private Material greenFaceMaterial;
    private Material darkMaterial;
    private Material roughMaterial;
    private Material flagMaterial;
    private Material hatBandMaterial;
    private Material arrowMaterial;
    private Material shoeMaterial;
    private Material whiteMaterial;
    private Material goldMetalMaterial;
    private Material putterFaceMaterial;

    private PhysicsMaterial curbPhysics;
    private PhysicsMaterial greenPhysics;
    private PhysicsMaterial ballPhysics;

    private Texture2D guiPixel;
    private enum MenuPage { Title, HowTo, Levels, Playing, Paused }
    private MenuPage menuPage = MenuPage.Title;
    private int level = 1;
    private int unlockedLevel;
    private AudioSource soundSource;
    private AudioSource musicSource;
    private AudioClip generatedMusic;
    private AudioClip hitClip, bounceClip;
    private bool muted;
    private float lastBounceTime;
    private Material woodMaterial;
    private Material windmillWhiteMaterial;
    private Material windmillRedMaterial;
    private Material windmillBlueMaterial;
    private Material accentMaterial;
    private Material glassMaterial;
    private Material confettiMaterial;
    private AudioClip trumpetClip, glassClip;
    private bool qualified;
    private bool washingDownstream;
    private const float GlassHeight = 1.9f;
    private static readonly int[] StrokeLimits = { 5, 6, 7, 10, 12 };
    private int StrokeLimit { get { return StrokeLimits[Mathf.Clamp(level - 1, 0, 4)]; } }

    private const float StartT = 0.075f;
    private const float HoleT = 0.91f;
    private float TeeT { get { return level == 4 ? 0f : StartT; } }

    private void Start()
    {
        unlockedLevel = 1;
        muted = PlayerPrefs.GetInt("MiniGolfMuted", 0) != 0;
        BuildRuntimeMaterials();
        SetupCamera();
        soundSource = gameObject.AddComponent<AudioSource>();
        soundSource.playOnAwake = false;
        generatedMusic = MakeBackgroundMusic();
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.clip = backgroundSong != null ? backgroundSong : generatedMusic;
        musicSource.volume = musicVolume;
        musicSource.mute = muted;
        musicSource.Play();
        hitClip = MakeClubImpact();
        bounceClip = MakeSound("Rubber wall", false);
        trumpetClip = MakeTrumpet();
        glassClip = MakeGlassCrash();
        LoadLevel(1);
        SetMenu(MenuPage.Title);
    }

    private void SetMenu(MenuPage page)
    {
        menuPage = page;
        bool playing = page == MenuPage.Playing;
        Time.timeScale = playing ? 1f : 0f;
        if (musicSource != null)
        {
            musicSource.mute = muted;
            musicSource.volume = musicVolume;
            AudioClip selectedMusic = backgroundSong != null ? backgroundSong : generatedMusic;
            if (musicSource.clip != selectedMusic)
            {
                musicSource.Stop();
                musicSource.clip = selectedMusic;
            }
            if (selectedMusic != null && !musicSource.isPlaying) musicSource.Play();
        }
        if (!playing) { charging = false; power01 = 0f; }
    }

    private void LoadLevel(int next)
    {
        StopAllCoroutines();
        level = Mathf.Clamp(next, 1, 5);
        if (runtimeRoot != null) Destroy(runtimeRoot);
        runtimeRoot = null;
        ballRb = null;
        playerRoot = null;
        clubPivot = null;
        flagPoleTransform = null;
        flagBannerTransform = null;
        aimArrowRoot = null;
        washingDownstream = false;
        BuildCourse();
        BuildBall();
        BuildPlayer();
        BuildAimArrow();
        RestartHole();
    }

    private void Update()
    {
        if (EscapePressed())
        {
            if (menuPage == MenuPage.Playing) SetMenu(MenuPage.Paused);
            else if (menuPage == MenuPage.Paused) SetMenu(MenuPage.Playing);
        }
        if (menuPage != MenuPage.Playing) return;
        if (strokePopupTimer > 0f)
            strokePopupTimer -= Time.deltaTime;

        if (RestartPressed())
        {
            LoadLevel(level);
            return;
        }

        HandleCameraInput();

        if (state == GameState.Aiming)
            HandleAimingInput();

        UpdateFlagLift();
        UpdateAimArrow();
    }

    private void FixedUpdate()
    {
        if (ballRb == null)
            return;

        if (state == GameState.BallMoving && !washingDownstream)
        {
            CheckForHole();

            if (state == GameState.BallMoving)
                CheckForBallStopped();

            if (ballRb.position.y < -2.5f)
                ResetBallToLastLie();
        }
    }

    private void LateUpdate()
    {
        if (gameCamera == null || ballRb == null)
            return;

        if (state == GameState.Aiming || state == GameState.Swinging)
            UpdateAimingCamera();
        else if (state == GameState.BallMoving)
            UpdateMovingCamera();
        else if (state == GameState.Holed)
            UpdateHoledCamera();
    }

    private void HandleAimingInput()
    {
        float rotate = 0f;
        if (LeftHeld())
            rotate -= 1f;
        if (RightHeld())
            rotate += 1f;

        if (!charging && Mathf.Abs(rotate) > 0.01f)
        {
            aimYaw += rotate * aimRotationSpeed * Time.deltaTime;
            PlacePlayerAtBall();
        }

        if (SpaceDown())
        {
            charging = true;
            chargeClock = 0f;
            power01 = 0f;
        }

        if (charging && SpaceHeld())
        {
            chargeClock += Time.deltaTime * powerMeterSpeed * 0.70f;
            power01 = Mathf.Clamp01(Mathf.PingPong(chargeClock, 1f));
        }

        if (charging && SpaceUp())
        {
            charging = false;
            StartCoroutine(SwingAndHit(power01));
        }
    }

    private IEnumerator SwingAndHit(float releasedPower)
    {
        state = GameState.Swinging;
        strokes++;
        strokePopupTimer = 1f;

        float backswingDuration = 0.10f;
        float downswingDuration = 0.14f;
        float returnDuration = 0.16f;

        Quaternion idle = Quaternion.Euler(18f, 0f, 0f);
        Quaternion back = Quaternion.Euler(18f, -58f, 0f);
        Quaternion through = Quaternion.Euler(18f, 75f, 0f);

        if (clubPivot != null)
        {
            float t = 0f;
            while (t < backswingDuration)
            {
                t += Time.deltaTime;
                clubPivot.localRotation = Quaternion.Slerp(idle, back, Mathf.Clamp01(t / backswingDuration));
                yield return null;
            }
        }

        lastLie = ballRb.position;

        bool ballHit = false;
        float swingTime = 0f;

        while (swingTime < downswingDuration)
        {
            swingTime += Time.deltaTime;
            float n = Mathf.Clamp01(swingTime / downswingDuration);

            if (clubPivot != null)
                clubPivot.localRotation = Quaternion.Slerp(back, through, n);

            if (!ballHit && n >= 0.42f)
            {
                ballHit = true;
                HitBall(releasedPower);
            }

            yield return null;
        }

        if (!ballHit)
            HitBall(releasedPower);

        if (clubPivot != null)
        {
            float t = 0f;
            while (t < returnDuration)
            {
                t += Time.deltaTime;
                clubPivot.localRotation = Quaternion.Slerp(through, idle, Mathf.Clamp01(t / returnDuration));
                yield return null;
            }

            clubPivot.localRotation = idle;
        }
    }

    private void HitBall(float releasedPower)
    {
        PlaySound(hitClip, 0.72f);
        float impulse = maximumShotImpulse * Mathf.Clamp01(releasedPower);
        Vector3 direction = AimDirection();

        followDirection = direction;
        holePreview = false;
        stoppedTimer = 0f;
        shotStartTime = Time.time;

        ballRb.isKinematic = false;
        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        ballRb.AddForce(direction * impulse, ForceMode.Impulse);
        ballRb.WakeUp();

        state = GameState.BallMoving;
    }

    private void CheckForBallStopped()
    {
        if (Time.time - shotStartTime < 0.3f)
            return;

        bool linearStopped = ballRb.linearVelocity.magnitude <= Mathf.Max(stopSpeed, 0.42f);
        bool angularStopped = ballRb.angularVelocity.magnitude <= Mathf.Max(stopAngularSpeed, 1.2f);

        if (linearStopped && angularStopped)
        {
            stoppedTimer += Time.fixedDeltaTime;

            if (stoppedTimer >= stopConfirmTime)
            {
                ballRb.linearVelocity = Vector3.zero;
                ballRb.angularVelocity = Vector3.zero;

                state = strokes >= StrokeLimit ? GameState.OutOfStrokes : GameState.Aiming;
                stoppedTimer = 0f;
                charging = false;
                power01 = 0f;

                PlacePlayerAtBall();
                UpdateAimArrow();
            }
        }
        else
        {
            stoppedTimer = 0f;
        }
    }

    public void EnterLevelFiveWater()
    {
        if (level != 5 || menuPage != MenuPage.Playing ||
            state != GameState.BallMoving || washingDownstream) return;
        ResetBallToBridgeEntrance();
    }

    private void ResetBallToBridgeEntrance()
    {
        washingDownstream = true;
        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        Vector3 to = GetCenterPoint(0.565f) + Vector3.up * (ballRadius + 0.06f);
        ballRb.position = to;
        Physics.SyncTransforms();
        lastLie = to;
        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        washingDownstream = false;
        state = strokes >= StrokeLimit ? GameState.OutOfStrokes : GameState.Aiming;
        stoppedTimer = 0f;
        PlacePlayerAtBall();
        UpdateAimArrow();
    }

    private void CheckForHole()
    {
        Vector3 ball = ballRb.position;
        Vector2 ballXZ = new Vector2(ball.x, ball.z);
        Vector2 holeXZ = new Vector2(holePosition.x, holePosition.z);
        float horizontalDistance = Vector2.Distance(ballXZ, holeXZ);

        if (horizontalDistance <= holeRadius * 0.80f &&
            ballRb.linearVelocity.magnitude <= holeCaptureMaxSpeed &&
            Mathf.Abs(ball.y - (holePosition.y + ballRadius)) < 0.9f)
        {
            StartCoroutine(SinkBall());
        }
    }

    private IEnumerator SinkBall()
    {
        if (state == GameState.Holed)
            yield break;

        state = GameState.Holed;
        qualified = strokes <= StrokeLimit;
        if (qualified && level < 5 && unlockedLevel < level + 1)
        {
            unlockedLevel = level + 1;
        }
        charging = false;
        power01 = 0f;
        PlaySound(trumpetClip, 0.8f);
        SpawnConfetti();

        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        ballRb.isKinematic = true;

        Vector3 from = ballRb.position;
        Vector3 to = holePosition + Vector3.down * (ballRadius * 1.45f);

        float duration = 0.45f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float n = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            ballRb.position = Vector3.Lerp(from, to, n);
            yield return null;
        }

        ballRb.position = to;
    }

    private void ResetBallToLastLie()
    {
        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        ballRb.position = lastLie;
        Physics.SyncTransforms();

        charging = false;
        power01 = 0f;
        stoppedTimer = 0f;
        state = strokes >= StrokeLimit ? GameState.OutOfStrokes : GameState.Aiming;

        PlacePlayerAtBall();
        UpdateAimArrow();
    }

    private void RestartHole()
    {
        StopAllCoroutines();

        strokes = 0;
        qualified = false;
        strokePopupTimer = 0f;
        charging = false;
        chargeClock = 0f;
        power01 = 0f;
        stoppedTimer = 0f;
        flagLiftLerp = 0f;
        cameraOrbitYaw = 0f;
        cameraOrbitPitch = 0f;
        holePreview = false;

        ballRb.isKinematic = false;
        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        ballRb.position = ballStartPosition;
        ballRb.rotation = Quaternion.identity;
        Physics.SyncTransforms();

        Vector3 openingDirection = GetCenterPoint(TeeT + 0.035f) - GetCenterPoint(TeeT);
        openingDirection.y = 0f;
        if (openingDirection.sqrMagnitude < 0.001f)
            openingDirection = Vector3.forward;

        aimYaw = Mathf.Atan2(openingDirection.normalized.x, openingDirection.normalized.z) * Mathf.Rad2Deg;
        followDirection = AimDirection();
        lastLie = ballStartPosition;

        if (clubPivot != null)
            clubPivot.localRotation = Quaternion.Euler(18f, 0f, 0f);

        state = GameState.Aiming;
        PlacePlayerAtBall();
        UpdateAimArrow();
        SnapCameraToAimingPosition();
        UpdateFlagLiftImmediate(0f);
    }

    private void PlacePlayerAtBall()
    {
        if (playerRoot == null || ballRb == null)
            return;

        Vector3 direction = AimDirection();
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 ball = ballRb.position;

        Vector3 stance = ball - direction * 1.05f - right * 0.72f;
        stance.y = ball.y - ballRadius + 0.02f;

        playerRoot.position = stance;
        playerRoot.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private Vector3 AimDirection()
    {
        return (Quaternion.Euler(0f, aimYaw, 0f) * Vector3.forward).normalized;
    }

    private void SetupCamera()
    {
        gameCamera = Camera.main;

        if (gameCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            gameCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        gameCamera.fieldOfView = 60f;
        gameCamera.nearClipPlane = 0.05f;
    }

    private void HandleCameraInput()
    {
        if (HolePreviewPressed() && state == GameState.Aiming)
            holePreview = !holePreview;

        if (ResetCameraPressed())
        {
            cameraOrbitYaw = 0f;
            cameraOrbitPitch = 0f;
            holePreview = false;

            if (state == GameState.Aiming || state == GameState.Swinging)
                SnapCameraToAimingPosition();
        }

        if (MouseLeftHeld())
        {
            Vector2 delta = MouseDelta();
            cameraOrbitYaw += delta.x * mouseOrbitSensitivity;
            cameraOrbitPitch -= delta.y * mousePitchSensitivity;
            cameraOrbitPitch = Mathf.Clamp(cameraOrbitPitch, -maxCameraPitch * 0.35f, maxCameraPitch);
        }
    }

    private Vector3 ApplyManualOrbit(Vector3 baseOffset)
    {
        Vector3 offset = Quaternion.AngleAxis(cameraOrbitYaw, Vector3.up) * baseOffset;

        Vector3 flat = new Vector3(offset.x, 0f, offset.z);
        if (flat.sqrMagnitude > 0.001f)
        {
            Vector3 pitchAxis = Vector3.Cross(Vector3.up, flat.normalized).normalized;
            offset = Quaternion.AngleAxis(cameraOrbitPitch, pitchAxis) * offset;
        }

        return offset;
    }

    private void SnapCameraToAimingPosition()
    {
        if (holePreview)
        {
            GetBallAndHoleCamera(out Vector3 previewPosition, out Vector3 previewFocus, true);
            gameCamera.transform.position = previewPosition;
            gameCamera.transform.LookAt(previewFocus);
            return;
        }

        Vector3 direction = AimDirection();
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 focus = ballRb.position + Vector3.up * 0.28f;

        Vector3 baseOffset =
            -direction * aimingCameraDistance +
            right * aimingCameraRightOffset +
            Vector3.up * aimingCameraHeight;

        gameCamera.transform.position = focus + ApplyManualOrbit(baseOffset);
        gameCamera.transform.LookAt(focus);
    }

    private void UpdateAimingCamera()
    {
        if (holePreview)
        {
            GetBallAndHoleCamera(out Vector3 previewPosition, out Vector3 previewFocus, true);
            SmoothMoveCamera(previewPosition, previewFocus);
            return;
        }

        Vector3 direction = AimDirection();
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 focus = ballRb.position + Vector3.up * 0.28f;

        Vector3 baseOffset =
            -direction * aimingCameraDistance +
            right * aimingCameraRightOffset +
            Vector3.up * aimingCameraHeight;

        Vector3 desired = focus + ApplyManualOrbit(baseOffset);
        SmoothMoveCamera(desired, focus);
    }

    private void GetBallAndHoleCamera(out Vector3 desired, out Vector3 focus, bool previewMode)
    {
        Vector3 ball = ballRb.position;
        Vector3 hole = holePosition + Vector3.up * 0.18f;
        Vector3 ballToHole = hole - ball;
        ballToHole.y = 0f;

        float separation = ballToHole.magnitude;
        Vector3 direction = separation > 0.01f ? ballToHole.normalized : followDirection.normalized;
        if (direction.sqrMagnitude < 0.001f)
            direction = AimDirection();

        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;

        // Looking at the midpoint keeps both the moving ball and the cup visible.
        focus = Vector3.Lerp(ball, hole, previewMode ? 0.56f : 0.50f) + Vector3.up * 0.24f;

        float distance = Mathf.Clamp(separation * 0.52f + (previewMode ? 5.5f : 4.8f), 6.5f, 20f);
        float height = Mathf.Clamp(separation * 0.25f + (previewMode ? 4.2f : 3.6f), 4f, 11f);
        float rightOffset = previewMode ? 1.8f : 1.25f;

        Vector3 baseOffset =
            -direction * distance +
            right * rightOffset +
            Vector3.up * height;

        desired = focus + ApplyManualOrbit(baseOffset);
    }

    private void UpdateMovingCamera()
    {
        Vector3 horizontalVelocity = ballRb.linearVelocity;
        horizontalVelocity.y = 0f;

        if (horizontalVelocity.sqrMagnitude > 0.04f)
        {
            Vector3 newDirection = horizontalVelocity.normalized;
            float directionBlend = 1f - Mathf.Exp(-4.5f * Time.deltaTime);
            followDirection = Vector3.Slerp(followDirection, newDirection, directionBlend);
        }

        // Follow the ball from above; fast shots must not outrun the frame.
        Vector3 focus = ballRb.position + Vector3.up * 0.22f;
        Vector3 direction = followDirection.sqrMagnitude > 0.001f ?
            followDirection.normalized : Vector3.forward;
        Vector3 desired = focus - direction * 3.2f + Vector3.up * 9f;
        float blend = 1f - Mathf.Exp(-Mathf.Max(12f, cameraSmooth) * Time.deltaTime);
        gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, desired, blend);
        if (Vector3.Distance(gameCamera.transform.position, desired) > 8f)
            gameCamera.transform.position = desired;
        gameCamera.transform.LookAt(focus);
    }

    private void UpdateHoledCamera()
    {
        Vector3 direction = followDirection.sqrMagnitude > 0.001f ? followDirection.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 focus = holePosition + Vector3.up * 0.35f;
        Vector3 baseOffset = -direction * 5.2f + right * 1.0f + Vector3.up * 3.2f;
        Vector3 desired = focus + ApplyManualOrbit(baseOffset);
        SmoothMoveCamera(desired, focus);
    }

    private void SmoothMoveCamera(Vector3 desiredPosition, Vector3 lookAt)
    {
        float blend = 1f - Mathf.Exp(-cameraSmooth * Time.deltaTime);
        gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, desiredPosition, blend);

        Quaternion desiredRotation = Quaternion.LookRotation(lookAt - gameCamera.transform.position, Vector3.up);
        gameCamera.transform.rotation = Quaternion.Slerp(gameCamera.transform.rotation, desiredRotation, blend);
    }

    private void UpdateAimArrow()
    {
        if (aimArrowRoot == null || ballRb == null)
            return;

        bool showArrow = (state == GameState.Aiming || state == GameState.Swinging);
        if (aimArrowRoot.gameObject.activeSelf != showArrow)
            aimArrowRoot.gameObject.SetActive(showArrow);

        if (!showArrow)
            return;

        Vector3 direction = AimDirection();
        float currentLength = arrowBaseLength + power01 * arrowPowerLength;

        aimArrowRoot.position = ballRb.position + Vector3.up * 0.18f + direction * (ballRadius + currentLength * 0.5f + 0.12f);
        aimArrowRoot.rotation = Quaternion.LookRotation(direction, Vector3.up);

        if (aimArrowShaft != null)
        {
            aimArrowShaft.localPosition = new Vector3(0f, 0f, -0.18f);
            aimArrowShaft.localScale = new Vector3(0.055f, 0.055f, currentLength);
        }

        if (aimArrowHead != null)
        {
            aimArrowHead.localPosition = new Vector3(0f, 0f, currentLength * 0.5f + 0.12f);
            aimArrowHead.localScale = new Vector3(0.15f, 0.15f, 0.22f);
        }

        arrowTipWorld = aimArrowRoot.TransformPoint(new Vector3(0f, 0f, currentLength * 0.5f + 0.35f));
    }

    private float CurrentPowerValue()
    {
        return maximumShotImpulse * power01;
    }

    private void UpdateFlagLift()
    {
        if (flagPoleTransform == null || flagBannerTransform == null || ballRb == null)
            return;

        Vector2 ballXZ = new Vector2(ballRb.position.x, ballRb.position.z);
        Vector2 holeXZ = new Vector2(holePosition.x, holePosition.z);
        float distance = Vector2.Distance(ballXZ, holeXZ);

        float target = distance <= flagTriggerDistance ? 1f : 0f;
        flagLiftLerp = Mathf.MoveTowards(flagLiftLerp, target, flagLiftSpeed * Time.deltaTime);

        ApplyFlagLift(flagLiftLerp * flagLiftAmount);
    }

    private void UpdateFlagLiftImmediate(float normalized)
    {
        flagLiftLerp = Mathf.Clamp01(normalized);
        ApplyFlagLift(flagLiftLerp * flagLiftAmount);
    }

    private void ApplyFlagLift(float lift)
    {
        flagPoleTransform.localPosition = flagPoleBaseLocalPosition + Vector3.up * lift;
        flagBannerTransform.localPosition = flagBannerBaseLocalPosition + Vector3.up * lift;
    }

    private void BuildRuntimeMaterials()
    {
        greenMaterial = CreateMaterial("Green", new Color(0.12f, 0.62f, 0.20f));
        curbMaterial = CreateMaterial("Curbs", new Color(0.90f, 0.90f, 0.92f));
        ballMaterial = CreateMaterial("Ball", new Color(0.98f, 0.98f, 0.98f));
        yellowSuitMaterial = CreateMaterial("Yellow Suit", new Color(0.96f, 0.78f, 0.12f));
        greenFaceMaterial = CreateMaterial("Green Face", new Color(0.50f, 0.74f, 0.22f));
        darkMaterial = CreateMaterial("Dark", new Color(0.035f, 0.035f, 0.045f));
        roughMaterial = CreateMaterial("Rough", new Color(0.08f, 0.30f, 0.10f));
        flagMaterial = CreateMaterial("Flag", new Color(0.88f, 0.10f, 0.10f));
        hatBandMaterial = CreateMaterial("Hat Band", new Color(0.20f, 0.20f, 0.22f));
        arrowMaterial = CreateTransparentMaterial("Aim Arrow", new Color(0.35f, 0.85f, 1f, 0.30f));
        shoeMaterial = CreateMaterial("Shoes", new Color(0.05f, 0.05f, 0.05f));
        whiteMaterial = CreateMaterial("White", new Color(0.97f, 0.97f, 0.97f));
        goldMetalMaterial = CreateMaterial("Gold Metal", new Color(0.88f, 0.72f, 0.22f));
        putterFaceMaterial = CreateMaterial("Putter Face", new Color(0.06f, 0.06f, 0.06f));
        woodMaterial = CreateMaterial("Wood Log", new Color(0.48f, 0.25f, 0.08f));
        windmillWhiteMaterial = CreateMaterial("Windmill White", new Color(0.93f, 0.94f, 0.91f));
        windmillRedMaterial = CreateMaterial("Windmill Roof Red", new Color(0.88f, 0.10f, 0.13f));
        windmillBlueMaterial = CreateMaterial("Windmill Lattice Blue", new Color(0.04f, 0.20f, 0.68f));
        accentMaterial = CreateMaterial("Course Accent", new Color(0.98f, 0.63f, 0.11f));
        glassMaterial = CreateTransparentMaterial("Safety Glass", new Color(0.67f, 0.90f, 1f, 0.24f));
        confettiMaterial = CreateMaterial("Confetti", Color.white);
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader != null) confettiMaterial.shader = particleShader;

        curbPhysics = new PhysicsMaterial("Curb Bounce")
        {
            bounciness = 0.82f,
            dynamicFriction = 0.08f,
            staticFriction = 0.08f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };

        greenPhysics = new PhysicsMaterial("Green Physics")
        {
            bounciness = 0.04f,
            dynamicFriction = 0.42f,
            staticFriction = 0.48f,
            bounceCombine = PhysicsMaterialCombine.Minimum,
            frictionCombine = PhysicsMaterialCombine.Average
        };

        ballPhysics = new PhysicsMaterial("Ball Physics")
        {
            bounciness = 0.20f,
            dynamicFriction = 0.18f,
            staticFriction = 0.18f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };
    }

    private Material CreateMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;
        return material;
    }

    private Material CreateTransparentMaterial(string materialName, Color color)
    {
        Material material = CreateMaterial(materialName, color);
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        material.color = color;
        return material;
    }

    private void BuildCourse()
    {
        runtimeRoot = new GameObject("MiniGolf Runtime");

        BuildRoughGround();
        BuildCurvedGreenAndCurbs();
        if (level == 4)
        {
            BuildRoundedCourseEnd(false);
            BuildRoundedCourseEnd(true);
        }
        if (level == 5) BuildLevelFiveBridge();
        BuildObstacles();
        BuildHoleAndFlag();
        BuildCourseDecorations();
    }

    private void BuildRoughGround()
    {
        GameObject rough = GameObject.CreatePrimitive(PrimitiveType.Plane);
        rough.name = "Background Rough";
        rough.transform.SetParent(runtimeRoot.transform);

        float length = CourseRunLength();
        rough.transform.position = new Vector3(2f, -0.12f, length * 0.5f);
        rough.transform.localScale = new Vector3(6.0f, 1f, Mathf.Max(4.2f, length / 10f + 1.0f));

        Renderer renderer = rough.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = roughMaterial;

        Collider collider = rough.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
    }

    private void BuildCourseDecorations()
    {
        Vector3 tee = GetCenterPoint(TeeT);
        GameObject teePad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        teePad.name = "Tee Circle";
        teePad.transform.SetParent(runtimeRoot.transform);
        teePad.transform.position = tee + Vector3.up * 0.012f;
        teePad.transform.localScale = new Vector3(0.95f, 0.01f, 0.95f);
        teePad.GetComponent<Renderer>().sharedMaterial = accentMaterial;
        DisableAndDestroyCollider(teePad);

        for (int i = 0; i < 18; i++)
        {
            float t = 0.08f + i * 0.048f;
            if (t < CoursePathStartT() || t > CoursePathEndT()) continue;
            Vector3 tangent = GetCenterPoint(Mathf.Min(1f, t + 0.01f)) -
                GetCenterPoint(Mathf.Max(0f, t - 0.01f));
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.0001f) continue;
            Vector3 right = Vector3.Cross(Vector3.up, tangent.normalized);
            Vector3 center = GetCenterPoint(t);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject flower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flower.name = "Course flower " + i + "-" + side;
                flower.transform.SetParent(runtimeRoot.transform);
                flower.transform.position = center + right * side * (courseWidth * 0.5f + 1.5f);
                flower.transform.position = new Vector3(flower.transform.position.x, 0.08f,
                    flower.transform.position.z);
                flower.transform.localScale = Vector3.one * (i % 3 == 0 ? 0.25f : 0.17f);
                flower.GetComponent<Renderer>().sharedMaterial =
                    i % 3 == 0 ? windmillRedMaterial : i % 3 == 1 ? accentMaterial : windmillBlueMaterial;
                DisableAndDestroyCollider(flower);
            }
            if (level < 4 || i % 2 != 0) continue;
            GetEdgePoints(t, out Vector3 left, out Vector3 rightEdge);
            for (int side = 0; side < 2; side++)
            {
                Vector3 stripe = Vector3.Lerp(left, rightEdge, side == 0 ? 0.12f : 0.88f);
                float u = side == 0 ? 0.12f : 0.88f;
                stripe.y += BankHeight(t) * (Mathf.Pow(Mathf.Abs(u * 2f - 1f), 2.4f) - 1f) + 0.035f;
                GameObject dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dash.name = "Half pipe edge marker";
                dash.transform.SetParent(runtimeRoot.transform);
                dash.transform.position = stripe;
                dash.transform.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                dash.transform.localScale = new Vector3(0.11f, 0.025f, 0.45f);
                dash.GetComponent<Renderer>().sharedMaterial = accentMaterial;
                DisableAndDestroyCollider(dash);
            }
        }
    }

    private void BuildCurvedGreenAndCurbs()
    {
        if (level == 2)
        {
            BuildRightAngleGreen();
            return;
        }
        GameObject green = new GameObject("Curved Green");
        green.transform.SetParent(runtimeRoot.transform);

        MeshFilter filter = green.AddComponent<MeshFilter>();
        MeshRenderer renderer = green.AddComponent<MeshRenderer>();
        MeshCollider collider = green.AddComponent<MeshCollider>();

        Mesh mesh = BuildGreenMesh();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = greenMaterial;
        collider.sharedMesh = mesh;
        collider.material = greenPhysics;

        int segments = CourseSegments();
        for (int i = 0; i < segments; i++)
        {
            float t0 = Mathf.Lerp(CoursePathStartT(), CoursePathEndT(), i / (float)segments);
            float t1 = Mathf.Lerp(CoursePathStartT(), CoursePathEndT(), (i + 1) / (float)segments);
            // The bridge replaces this span, including its outer banks.
            if (level == 5 && (t0 + t1) * 0.5f >= 0.59f &&
                (t0 + t1) * 0.5f <= 0.69f) continue;

            GetEdgePoints(t0, out Vector3 left0, out Vector3 right0);
            GetEdgePoints(t1, out Vector3 left1, out Vector3 right1);

            CreateCurbSegment(left0, left1, "Left Curb " + i);
            CreateCurbSegment(right0, right1, "Right Curb " + i);
            CreateGlassPane(left0, left1, "Left Glass " + i);
            CreateGlassPane(right0, right1, "Right Glass " + i);
        }

        if (level != 4)
        {
            GetEdgePoints(0f, out Vector3 startLeft, out Vector3 startRight);
            if (level == 5) CreateSolidEndWall(startLeft, startRight, "Start retaining wall");
            else
            {
                CreateCurbSegment(startLeft, startRight, "Start Curb");
                CreateGlassPane(startLeft, startRight, "Start Glass");
            }
        }
        if (level != 4)
        {
            GetEdgePoints(1f, out Vector3 endLeft, out Vector3 endRight);
            if (level == 5) CreateSolidEndWall(endLeft, endRight, "End retaining wall");
            else
            {
                CreateCurbSegment(endLeft, endRight, "End Curb");
                CreateGlassPane(endLeft, endRight, "End Glass");
            }
        }
    }

    private float CoursePathStartT() { return 0f; }
    private float CoursePathEndT() { return 1f; }

    private void CreateSolidEndWall(Vector3 a, Vector3 b, string name)
    {
        Vector3 direction = b - a;
        direction.y = 0f;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(runtimeRoot.transform);
        float baseHeight = Mathf.Min(a.y, b.y);
        if (level == 5 && name.Contains("retaining wall"))
            baseHeight -= BankHeight(name.StartsWith("End") ? 1f : 0f);
        wall.transform.position = new Vector3((a.x + b.x) * 0.5f,
            baseHeight + 1.15f, (a.z + b.z) * 0.5f);
        wall.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        wall.transform.localScale = new Vector3(0.42f, 2.3f, direction.magnitude + 0.5f);
        wall.GetComponent<Renderer>().sharedMaterial = curbMaterial;
        wall.GetComponent<Collider>().material = curbPhysics;
    }

    private Vector3 CourseRight(float t)
    {
        Vector3 tangent = GetCenterPoint(t + 0.005f) - GetCenterPoint(t - 0.005f);
        tangent.y = 0f;
        return Vector3.Cross(Vector3.up, tangent.normalized);
    }

    private void BuildLevelFiveBridge()
    {
        // One smoothly sampled deck and collider, with overlap at each bank.
        const int bridgeSegments = 48;
        Vector3[] deckVertices = new Vector3[(bridgeSegments + 1) * 2];
        int[] deckTriangles = new int[bridgeSegments * 6];
        for (int i = 0; i <= bridgeSegments; i++)
        {
            float t = Mathf.Lerp(0.585f, 0.695f, i / (float)bridgeSegments);
            Vector3 center = GetCenterPoint(t) + Vector3.up * 0.006f;
            Vector3 right = CourseRight(t);
            deckVertices[i * 2] = center - right * 1.12f;
            deckVertices[i * 2 + 1] = center + right * 1.12f;
            if (i == bridgeSegments) continue;
            int k = i * 6;
            int v = i * 2;
            deckTriangles[k] = v;
            deckTriangles[k + 1] = v + 2;
            deckTriangles[k + 2] = v + 1;
            deckTriangles[k + 3] = v + 1;
            deckTriangles[k + 4] = v + 2;
            deckTriangles[k + 5] = v + 3;
        }
        Mesh bridgeMesh = new Mesh();
        bridgeMesh.name = "Continuous bridge deck";
        bridgeMesh.vertices = deckVertices;
        bridgeMesh.triangles = deckTriangles;
        bridgeMesh.RecalculateNormals();
        bridgeMesh.RecalculateBounds();
        GameObject deck = new GameObject("Continuous narrow bridge");
        deck.transform.SetParent(runtimeRoot.transform);
        deck.AddComponent<MeshFilter>().sharedMesh = bridgeMesh;
        deck.AddComponent<MeshRenderer>().sharedMaterial = woodMaterial;
        MeshCollider deckCollider = deck.AddComponent<MeshCollider>();
        deckCollider.sharedMesh = bridgeMesh;
        deckCollider.material = greenPhysics;
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < bridgeSegments; i++)
            {
                float t0 = Mathf.Lerp(0.585f, 0.695f, i / (float)bridgeSegments);
                float t1 = Mathf.Lerp(0.585f, 0.695f, (i + 1f) / bridgeSegments);
                Vector3 a = GetCenterPoint(t0) + CourseRight(t0) * (side * 1.15f);
                Vector3 b = GetCenterPoint(t1) + CourseRight(t1) * (side * 1.15f);
                Vector3 tangent = b - a;
                GameObject railing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                railing.name = "Bridge side railing " + side + "-" + i;
                railing.transform.SetParent(runtimeRoot.transform);
                railing.transform.position = (a + b) * 0.5f + Vector3.up * 0.38f;
                railing.transform.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                railing.transform.localScale = new Vector3(0.16f, 0.76f, tangent.magnitude + 0.045f);
                railing.GetComponent<Renderer>().sharedMaterial = curbMaterial;
                railing.GetComponent<Collider>().material = curbPhysics;
            }
        }

        Vector3 waterCenter = GetCenterPoint(0.64f);
        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cube);
        water.name = "Downstream water";
        water.transform.SetParent(runtimeRoot.transform);
        water.transform.position = new Vector3(waterCenter.x, 0.40f, waterCenter.z);
        water.transform.localScale = new Vector3(11f, 0.08f, 8f);
        water.GetComponent<Renderer>().sharedMaterial =
            CreateMaterial("River blue", new Color(0.06f, 0.53f, 0.75f));
        DisableAndDestroyCollider(water);

        GameObject current = GameObject.CreatePrimitive(PrimitiveType.Cube);
        current.name = "Water current trigger";
        current.transform.SetParent(runtimeRoot.transform);
        current.transform.position = new Vector3(waterCenter.x, -0.12f, waterCenter.z);
        current.transform.localScale = new Vector3(11f, 1.05f, 8f);
        current.GetComponent<Renderer>().enabled = false;
        current.GetComponent<Collider>().isTrigger = true;
        current.AddComponent<MiniGolfWater>().owner = this;

        Vector3 rough = GetCenterPoint(0.72f) + CourseRight(0.72f) *
            (courseWidth * 0.5f + 0.55f);
        GameObject landing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        landing.name = "Downstream rough landing and return path";
        landing.transform.SetParent(runtimeRoot.transform);
        landing.transform.position = rough + Vector3.down * 0.08f;
        landing.transform.rotation = Quaternion.LookRotation(
            GetCenterPoint(0.73f) - GetCenterPoint(0.71f), Vector3.up);
        landing.transform.localScale = new Vector3(4.3f, 0.16f, 6.5f);
        landing.GetComponent<Renderer>().sharedMaterial = roughMaterial;
        landing.GetComponent<Collider>().material = greenPhysics;
        for (int side = -1; side <= 1; side += 2)
        {
            if (side < 0) continue; // Leave the course-facing side open.
            Vector3 edge = rough + CourseRight(0.72f) * 2.1f;
            Vector3 along = GetCenterPoint(0.76f) - GetCenterPoint(0.68f);
            CreateSolidEndWall(edge - along * 0.5f, edge + along * 0.5f,
                "Downstream rough outer wall");
        }
    }

    private void BuildRoundedCourseEnd(bool finish)
    {
        float t = finish ? 1f : 0f;
        Vector3 center = GetCenterPoint(t);
        Vector3 tangent = GetCenterPoint(finish ? 1f : 0.01f) -
            GetCenterPoint(finish ? 0.99f : 0f);
        tangent.y = 0f;
        tangent.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, tangent);
        Vector3 outward = finish ? tangent : -tangent;
        GetEdgePoints(t, out Vector3 leftEdge, out Vector3 rightEdge);
        float radius = Vector3.Distance(leftEdge, rightEdge) * 0.5f;
        const int rings = 12;
        const int slices = 48;
        Vector3[] vertices = new Vector3[(rings + 1) * (slices + 1)];
        int[] triangles = new int[rings * slices * 6];
        for (int ring = 0; ring <= rings; ring++)
        {
            float r = radius * ring / rings;
            for (int slice = 0; slice <= slices; slice++)
            {
                float angle = -Mathf.PI * 0.5f + Mathf.PI * slice / slices;
                Vector3 radial = outward * Mathf.Cos(angle) + right * Mathf.Sin(angle);
                vertices[ring * (slices + 1) + slice] = center + radial * r +
                    Vector3.up * (BankHeight(t) * Mathf.Pow(r / radius, 2.4f));
                if (ring == rings || slice == slices) continue;
                int inner = ring * (slices + 1) + slice;
                int outer = inner + slices + 1;
                int k = (ring * slices + slice) * 6;
                if (finish)
                {
                    triangles[k] = inner;
                    triangles[k + 1] = outer;
                    triangles[k + 2] = inner + 1;
                    triangles[k + 3] = inner + 1;
                    triangles[k + 4] = outer;
                    triangles[k + 5] = outer + 1;
                }
                else
                {
                    triangles[k] = inner;
                    triangles[k + 1] = inner + 1;
                    triangles[k + 2] = outer;
                    triangles[k + 3] = inner + 1;
                    triangles[k + 4] = outer + 1;
                    triangles[k + 5] = outer;
                }
            }
        }
        Mesh mesh = new Mesh();
        mesh.name = finish ? "Rounded finish floor" : "Rounded tee floor";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GameObject cap = new GameObject(mesh.name);
        cap.transform.SetParent(runtimeRoot.transform);
        cap.AddComponent<MeshFilter>().sharedMesh = mesh;
        cap.AddComponent<MeshRenderer>().sharedMaterial = greenMaterial;
        MeshCollider collider = cap.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.material = greenPhysics;
        for (int slice = 0; slice < slices; slice++)
        {
            Vector3 p = vertices[rings * (slices + 1) + slice];
            Vector3 q = vertices[rings * (slices + 1) + slice + 1];
            CreateCurbSegment(p, q, mesh.name + " curb " + slice);
            CreateGlassPane(p, q, mesh.name + " glass " + slice);
        }
    }

    private void BuildRightAngleGreen()
    {
        float half = courseWidth * 0.5f;
        float turnZ = 11.2f;
        float finishX = 22f;
        // Two adjoining lanes form a single sharp right turn.
        CreateLevel2Strip("Approach", -half, half, 0f, turnZ + half, 16, false);
        CreateLevel2Strip("Hill and finish", half, finishX,
            turnZ - half, turnZ + half, 40, true);

        Vector3[] outline = {
            new Vector3(-half, 0f, 0f), new Vector3(-half, 0f, turnZ + half),
            new Vector3(finishX, 0f, turnZ + half),
            new Vector3(finishX, 0f, turnZ - half),
            new Vector3(half, 0f, turnZ - half),
            new Vector3(half, 0f, 0f)
        };
        for (int i = 0; i < outline.Length; i++)
        {
            Vector3 a = outline[i], b = outline[(i + 1) % outline.Length];
            int divisions = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 1.6f));
            for (int j = 0; j < divisions; j++)
            {
                Vector3 p = Vector3.Lerp(a, b, j / (float)divisions);
                Vector3 q = Vector3.Lerp(a, b, (j + 1f) / divisions);
                if (p.x >= half && q.x >= half)
                {
                    p.y = Level2Hill(p.x);
                    q.y = Level2Hill(q.x);
                }
                CreateCurbSegment(p, q, "Course Curb " + i + "-" + j);
                CreateGlassPane(p, q, "Course Glass " + i + "-" + j);
            }
        }
    }

    private float Level2Hill(float x)
    {
        return 3f * Mathf.Exp(-Mathf.Pow((x - 12.5f) / 3.0f, 2f));
    }

    private void CreateLevel2Strip(string name, float xMin, float xMax,
        float zMin, float zMax, int segments, bool hill)
    {
        GameObject green = new GameObject(name + " Green");
        green.transform.SetParent(runtimeRoot.transform);
        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        int[] triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float z = Mathf.Lerp(zMin, zMax, i / (float)segments);
            float x = Mathf.Lerp(xMin, xMax, i / (float)segments);
            float y = hill ? Level2Hill(x) : 0f;
            vertices[2 * i] = hill ? new Vector3(x, y, zMin) : new Vector3(xMin, y, z);
            vertices[2 * i + 1] = hill ? new Vector3(x, y, zMax) : new Vector3(xMax, y, z);
            if (i == segments) continue;
            int v = i * 2, k = i * 6;
            triangles[k] = v;
            triangles[k + 1] = hill ? v + 1 : v + 2;
            triangles[k + 2] = hill ? v + 2 : v + 1;
            triangles[k + 3] = hill ? v + 2 : v + 1;
            triangles[k + 4] = hill ? v + 1 : v + 2;
            triangles[k + 5] = v + 3;
        }
        Mesh mesh = new Mesh();
        mesh.name = name + " Mesh";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        green.AddComponent<MeshFilter>().sharedMesh = mesh;
        green.AddComponent<MeshRenderer>().sharedMaterial = greenMaterial;
        MeshCollider collider = green.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.material = greenPhysics;
    }

    private Mesh BuildGreenMesh()
    {
        int segments = CourseSegments();
        int across = level >= 4 ? 9 : 2;
        int vertexCount = (segments + 1) * across;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        List<int> triangles = new List<int>(segments * (across - 1) * 6);

        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.Lerp(CoursePathStartT(), CoursePathEndT(), i / (float)segments);
            GetEdgePoints(t, out Vector3 left, out Vector3 right);
            for (int j = 0; j < across; j++)
            {
                float u = j / (float)(across - 1);
                int v = i * across + j;
                vertices[v] = Vector3.Lerp(left, right, u);
                // Both walls rise smoothly like the sides of a snowboard half pipe.
                if (level >= 4)
                    vertices[v].y += BankHeight(t) * (Mathf.Pow(Mathf.Abs(u * 2f - 1f), 2.4f) - 1f);
                uvs[v] = new Vector2(u, t);
                if (i == segments || j == across - 1) continue;
                float midpoint = Mathf.Lerp(CoursePathStartT(), CoursePathEndT(),
                    (i + 0.5f) / segments);
                if (level == 5 && midpoint >= 0.59f && midpoint <= 0.69f) continue;
                int next = v + across;
                triangles.Add(v);
                triangles.Add(next);
                triangles.Add(v + 1);
                triangles.Add(v + 1);
                triangles.Add(next);
                triangles.Add(next + 1);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "Curved Mini Golf Green";
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private void GetEdgePoints(float t, out Vector3 left, out Vector3 right)
    {
        Vector3 center = GetCenterPoint(t);

        float delta = 1f / Mathf.Max(20f, CourseSegments());
        Vector3 before = GetCenterPoint(Mathf.Clamp01(t - delta));
        Vector3 after = GetCenterPoint(Mathf.Clamp01(t + delta));
        Vector3 tangent = after - before;

        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.forward;

        tangent.y = 0f;
        tangent.Normalize();

        Vector3 rightDirection = Vector3.Cross(Vector3.up, tangent).normalized;
        float halfWidth = courseWidth * 0.5f;
        if (level == 4)
        {
            float endShape = Mathf.Max(1f - Mathf.SmoothStep(0f, 0.20f, t),
                Mathf.SmoothStep(0.80f, 1f, t));
            halfWidth += 2.25f * endShape;
        }
        left = center - rightDirection * halfWidth;
        right = center + rightDirection * halfWidth;
        if (level >= 4)
        {
            left.y += BankHeight(t);
            right.y += BankHeight(t);
        }
    }

    private int CourseSegments() { return level >= 4 ? Mathf.Max(curveSegments, 96) : curveSegments; }
    private float CourseRunLength()
    {
        if (level == 4) return Mathf.Max(courseLength, 40f);
        if (level == 5) return Mathf.Max(courseLength, 49f);
        return courseLength;
    }
    private float BankHeight(float t)
    {
        if (level == 4) return 1.45f + 0.25f * Mathf.Sin(t * Mathf.PI * 2f);
        if (level == 5) return 1.85f + 0.35f * Mathf.Sin(t * Mathf.PI * 3f);
        return 0f;
    }

    private Vector3 GetCenterPoint(float t)
    {
        t = Mathf.Clamp01(t);

        float x = curveAmount * (0.5f - 0.5f * Mathf.Cos(Mathf.PI * t));
        if (level == 2)
        {
            float distance = t * (11.2f + 22f);
            if (distance <= 11.2f) return new Vector3(0f, 0f, distance);
            float xFinal = distance - 11.2f;
            return new Vector3(xFinal, Level2Hill(xFinal), 11.2f);
        }
        else if (level == 3) x = 3f * Mathf.Sin(t * Mathf.PI * 2f);
        else if (level == 4) x = 4.6f * Mathf.Sin(t * Mathf.PI * 2.5f);
        else if (level == 5) x = 5.2f * Mathf.Sin(t * Mathf.PI * 3.5f);
        float z = CourseRunLength() * t;
        float y = 0f;
        if (level == 4) y = 1.8f * Mathf.Exp(-Mathf.Pow((t - 0.48f) / 0.15f, 2f));
        else if (level == 5) y = 2f * Mathf.Exp(-Mathf.Pow((t - 0.60f) / 0.15f, 2f)) +
            3.4f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.76f) / 0.19f)) +
            1.3f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.55f) / 0.18f)), 2f);
        return new Vector3(x, y, z);
    }

    private void BuildObstacles()
    {
        if (level == 1) return;
        if (level == 2)
        {
            // Opposite side of the turn: shoot toward the far left bank to
            // rebound across the corner and save a stroke.
            CreateObstacle("Far side bank log", new Vector3(-1.5f, 0.31f, 12.8f),
                new Vector3(0.20f, 0.55f, 2.15f), 45f, woodMaterial);
        }
        if (level == 3)
        {
            CreateObstacleAt("Left bumper", 0.26f, -1.3f, 1.6f, 30f);
            BuildWindmill(0.53f);
            CreateBreakableObstacle("Glass prize gate", 0.75f, 0f, new Vector3(0.18f, 0.9f, 1.1f), 22f);
        }
        if (level == 4)
        {
            CreateBreakableObstacle("Glass zigzag A", 0.32f, -0.7f, new Vector3(0.16f, 0.85f, 1.8f), 42f);
            CreateMovableObstacle("Rolling wooden crate", 0.49f, 1.0f, 1.2f);
            CreateBreakableObstacle("Glass zigzag B", 0.68f, 0.8f, new Vector3(0.16f, 0.85f, 1.8f), -42f);
            CreateObstacleAt("High side bank target", 0.82f, 0.1f, 1.1f, 50f);
        }
        if (level == 5)
        {
            CreateMovableObstacle("Domino A", 0.27f, -0.9f, 0.9f);
            CreateMovableObstacle("Domino B", 0.40f, 0.9f, 0.9f);
            CreateBreakableObstacle("Glass barricade", 0.56f, 0f, new Vector3(0.18f, 1.0f, 1.9f), 50f);
            CreateMovableObstacle("Domino C", 0.72f, -0.7f, 0.9f);
            BuildSweeper(0.83f, 90f);
        }
    }

    private void CreateObstacleAt(string name, float t, float offset, float length, float angle)
    {
        Vector3 p = GetCenterPoint(t) + new Vector3(offset, 0.40f + SurfaceBankAtOffset(t, offset), 0f);
        CreateObstacle(name, p, new Vector3(0.5f, 0.75f, length), angle, woodMaterial);
    }

    private float SurfaceBankAtOffset(float t, float offset)
    {
        if (level < 4) return 0f;
        return BankHeight(t) * Mathf.Pow(Mathf.Clamp01(Mathf.Abs(offset) /
            (courseWidth * 0.5f)), 2.4f);
    }

    private void CreateObstacle(string name, Vector3 position, Vector3 scale, float angle, Material material)
    {
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = name;
        obstacle.transform.SetParent(runtimeRoot.transform);
        obstacle.transform.position = position;
        obstacle.transform.rotation = Quaternion.Euler(0f, angle, 0f);
        obstacle.transform.localScale = scale;
        obstacle.GetComponent<Renderer>().sharedMaterial = material;
        obstacle.GetComponent<Collider>().material = curbPhysics;
    }

    private void BuildWindmill(float t)
    {
        Vector3 center = GetCenterPoint(t);
        Vector3 tangent = GetCenterPoint(t + 0.01f) - GetCenterPoint(t - 0.01f);
        tangent.y = 0f;
        GameObject mill = new GameObject("Windmill Tunnel");
        mill.transform.SetParent(runtimeRoot.transform);
        mill.transform.position = center;
        mill.transform.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
        // Side supports leave a 1.25-unit opening, just over two ball diameters.
        CreateMillPart(mill.transform, "Left White Arch", new Vector3(-0.83f, 0.65f, 0f),
            new Vector3(0.36f, 1.3f, 2.9f), windmillWhiteMaterial);
        CreateMillPart(mill.transform, "Right White Arch", new Vector3(0.83f, 0.65f, 0f),
            new Vector3(0.36f, 1.3f, 2.9f), windmillWhiteMaterial);
        CreateMillPart(mill.transform, "Left Entrance Wing", new Vector3(-1.88f, 0.55f, -1.12f),
            new Vector3(1.65f, 1.1f, 0.38f), windmillWhiteMaterial);
        CreateMillPart(mill.transform, "Right Entrance Wing", new Vector3(1.88f, 0.55f, -1.12f),
            new Vector3(1.65f, 1.1f, 0.38f), windmillWhiteMaterial);
        BuildWindmillTower(mill.transform);
        BuildWindmillRoof(mill.transform);

        GameObject rotor = new GameObject("Rotating Windmill Blades");
        rotor.transform.SetParent(mill.transform);
        rotor.transform.localPosition = new Vector3(0f, 2.52f, -1.35f);
        rotor.transform.localRotation = Quaternion.identity;
        Rigidbody rotorBody = rotor.AddComponent<Rigidbody>();
        rotorBody.isKinematic = true;
        rotorBody.useGravity = false;
        MiniGolfWindmill spinning = rotor.AddComponent<MiniGolfWindmill>();
        spinning.degreesPerSecond = 62f;
        for (int i = 0; i < 4; i++)
        {
            GameObject blade = new GameObject("Blue lattice blade " + i);
            blade.transform.SetParent(rotor.transform);
            blade.transform.localPosition = Vector3.zero;
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
            BuildLatticeBlade(blade.transform);
        }
        GameObject hub = CreateMillPart(rotor.transform, "Blue windmill hub", Vector3.zero,
            new Vector3(0.36f, 0.36f, 0.22f), windmillBlueMaterial);
        DisableAndDestroyCollider(hub);
    }

    private void BuildWindmillTower(Transform parent)
    {
        const int slices = 24;
        for (int tier = 0; tier < 2; tier++)
        {
            float bottom = tier == 0 ? 0f : 1.15f;
            float top = tier == 0 ? 1.15f : 2.75f;
            for (int i = 0; i < slices; i++)
            {
                float a = i * Mathf.PI * 2f / slices;
                float b = (i + 1) * Mathf.PI * 2f / slices;
                float mid = (a + b) * 0.5f;
                // Open front and rear doors at ball height.
                if (tier == 0 && Mathf.Abs(Mathf.Cos(mid)) < 0.52f) continue;
                float radiusLow = Mathf.Lerp(1.2f, 0.88f, bottom / 2.75f);
                float radiusHigh = Mathf.Lerp(1.2f, 0.88f, top / 2.75f);
                Vector3[] quad = {
                    new Vector3(radiusLow * Mathf.Cos(a), bottom, radiusLow * Mathf.Sin(a)),
                    new Vector3(radiusLow * Mathf.Cos(b), bottom, radiusLow * Mathf.Sin(b)),
                    new Vector3(radiusHigh * Mathf.Cos(a), top, radiusHigh * Mathf.Sin(a)),
                    new Vector3(radiusHigh * Mathf.Cos(b), top, radiusHigh * Mathf.Sin(b))
                };
                CreateDecorativeMesh(parent, "White tapered tower panel", quad,
                    new int[] { 0, 2, 1, 1, 2, 3 }, windmillWhiteMaterial);
            }
        }
    }

    private void BuildWindmillRoof(Transform parent)
    {
        const int slices = 24;
        Vector3[] vertices = new Vector3[slices + 1];
        int[] tris = new int[slices * 3];
        vertices[0] = new Vector3(0f, 3.48f, 0f);
        for (int i = 0; i < slices; i++)
        {
            float angle = i * Mathf.PI * 2f / slices;
            vertices[i + 1] = new Vector3(1.46f * Mathf.Cos(angle), 2.72f,
                1.46f * Mathf.Sin(angle));
            tris[i * 3] = 0;
            tris[i * 3 + 1] = (i + 1) % slices + 1;
            tris[i * 3 + 2] = i + 1;
        }
        CreateDecorativeMesh(parent, "Red conical roof", vertices, tris, windmillRedMaterial);
    }

    private void BuildLatticeBlade(Transform parent)
    {
        for (int i = 0; i < 5; i++)
        {
            float x = -0.34f + i * 0.17f;
            CreateDecorativeBox(parent, "Blue vertical lattice", new Vector3(x, 1.0f, 0f),
                new Vector3(0.035f, 1.55f, 0.045f), windmillBlueMaterial);
        }
        for (int i = 0; i < 10; i++)
        {
            float y = 0.23f + i * 0.17f;
            CreateDecorativeBox(parent, "Blue horizontal lattice", new Vector3(0f, y, 0f),
                new Vector3(0.72f, 0.035f, 0.045f), windmillBlueMaterial);
        }
    }

    private void CreateDecorativeMesh(Transform parent, string name,
        Vector3[] vertices, int[] triangles, Material material)
    {
        Mesh mesh = new Mesh();
        mesh.name = name;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private void CreateDecorativeBox(Transform parent, string name, Vector3 position,
        Vector3 size, Material material)
    {
        GameObject part = CreateMillPart(parent, name, position, size, material);
        DisableAndDestroyCollider(part);
    }

    private GameObject CreateMillPart(Transform parent, string name, Vector3 localPosition,
        Vector3 size, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent);
        part.transform.localPosition = localPosition;
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = material;
        part.GetComponent<Collider>().material = curbPhysics;
        return part;
    }

    private void CreateBreakableObstacle(string name, float t, float sideOffset, Vector3 size, float angle)
    {
        Vector3 center = GetCenterPoint(t);
        Vector3 tangent = GetCenterPoint(t + 0.01f) - GetCenterPoint(t - 0.01f);
        tangent.y = 0f;
        Vector3 right = Vector3.Cross(Vector3.up, tangent.normalized);
        GameObject pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pane.name = name;
        pane.transform.SetParent(runtimeRoot.transform);
        pane.transform.position = center + right * sideOffset +
            Vector3.up * (size.y * 0.5f + SurfaceBankAtOffset(t, sideOffset));
        pane.transform.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up) *
            Quaternion.Euler(0f, angle, 0f);
        pane.transform.localScale = size;
        pane.GetComponent<Renderer>().sharedMaterial = glassMaterial;
        MiniGolfGlass glass = pane.AddComponent<MiniGolfGlass>();
        glass.owner = this;
        glass.breakThreshold = 1.2f;
    }

    private void CreateMovableObstacle(string name, float t, float sideOffset, float height)
    {
        Vector3 center = GetCenterPoint(t);
        Vector3 tangent = GetCenterPoint(t + 0.01f) - GetCenterPoint(t - 0.01f);
        tangent.y = 0f;
        Vector3 right = Vector3.Cross(Vector3.up, tangent.normalized);
        GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crate.name = name;
        crate.transform.SetParent(runtimeRoot.transform);
        crate.transform.position = center + right * sideOffset +
            Vector3.up * (height * 0.5f + 0.04f + SurfaceBankAtOffset(t, sideOffset));
        crate.transform.localScale = new Vector3(0.62f, height, 0.62f);
        crate.GetComponent<Renderer>().sharedMaterial = woodMaterial;
        Rigidbody body = crate.AddComponent<Rigidbody>();
        body.mass = 0.8f;
        body.linearDamping = 1.4f;
        body.angularDamping = 1.2f;
    }

    private void BuildSweeper(float t, float speed)
    {
        Vector3 center = GetCenterPoint(t);
        GameObject pivot = new GameObject("Spinning sweep gate");
        pivot.transform.SetParent(runtimeRoot.transform);
        pivot.transform.position = center + Vector3.up * 0.6f;
        Rigidbody body = pivot.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        MiniGolfSweeper spinner = pivot.AddComponent<MiniGolfSweeper>();
        spinner.degreesPerSecond = speed;
        CreateMillPart(pivot.transform, "Sweeper arm", Vector3.zero,
            new Vector3(courseWidth * 0.78f, 0.22f, 0.24f), accentMaterial);
        GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hub.name = "Sweeper hub";
        hub.transform.SetParent(pivot.transform);
        hub.transform.localPosition = Vector3.zero;
        hub.transform.localScale = new Vector3(0.30f, 0.25f, 0.30f);
        hub.GetComponent<Renderer>().sharedMaterial = windmillBlueMaterial;
        DisableAndDestroyCollider(hub);
    }

    private void CreateCurbSegment(Vector3 a, Vector3 b, string objectName)
    {
        Vector3 direction = b - a;
        float length = direction.magnitude;

        if (length <= 0.001f)
            return;

        GameObject curb = GameObject.CreatePrimitive(PrimitiveType.Cube);
        curb.name = objectName;
        curb.transform.SetParent(runtimeRoot.transform);

        curb.transform.position = (a + b) * 0.5f + Vector3.up * (curbHeight * 0.5f + 0.01f);
        curb.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        curb.transform.localScale = new Vector3(curbWidth, curbHeight, length + 0.04f);

        Renderer renderer = curb.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = curbMaterial;

        Collider collider = curb.GetComponent<Collider>();
        if (collider != null)
            collider.material = curbPhysics;
    }

    private void CreateGlassPane(Vector3 a, Vector3 b, string objectName)
    {
        Vector3 edge = b - a;
        if (edge.magnitude < 0.01f) return;
        GameObject pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pane.name = objectName;
        pane.transform.SetParent(runtimeRoot.transform);
        pane.transform.position = (a + b) * 0.5f + Vector3.up * (curbHeight + GlassHeight * 0.5f);
        pane.transform.rotation = Quaternion.LookRotation(edge.normalized, Vector3.up);
        pane.transform.localScale = new Vector3(0.07f, GlassHeight, edge.magnitude + 0.06f);
        pane.GetComponent<Renderer>().sharedMaterial = glassMaterial;
        MiniGolfGlass glass = pane.AddComponent<MiniGolfGlass>();
        glass.owner = this;
    }

    public void BreakGlass(GameObject pane, ContactPoint impact, float speed)
    {
        if (pane == null || menuPage != MenuPage.Playing) return;
        PlaySound(glassClip, Mathf.Clamp(speed / 14f, 0.3f, 0.9f));
        Vector3 pos = pane.transform.position;
        Quaternion rotation = pane.transform.rotation;
        Vector3 size = pane.transform.localScale;
        Vector3 away = -impact.normal;
        if (Vector3.Dot(away, ballRb.linearVelocity) < 0f) away = impact.normal;
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 2; col++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Falling Glass Shard";
            shard.transform.SetParent(runtimeRoot.transform);
            shard.transform.rotation = rotation * Quaternion.Euler(Random.Range(-9f, 9f), Random.Range(-12f, 12f), 0f);
            shard.transform.position = pos + rotation * new Vector3(0f,
                ((row + 0.5f) / 3f - 0.5f) * size.y, ((col + 0.5f) / 2f - 0.5f) * size.z);
            shard.transform.localScale = new Vector3(size.x, size.y / 3f * 0.93f, size.z / 2f * 0.93f);
            shard.GetComponent<Renderer>().sharedMaterial = glassMaterial;
            Rigidbody piece = shard.AddComponent<Rigidbody>();
            piece.mass = 0.08f;
            piece.AddForce((away + Vector3.up * 0.4f + Random.insideUnitSphere * 0.35f).normalized *
                Random.Range(1.5f, 3.5f), ForceMode.Impulse);
            piece.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);
            // Debris stays on the course until the player restarts the level.
        }
        pane.SetActive(false);
        Destroy(pane);
    }

    private void BuildHoleAndFlag()
    {
        Vector3 center = GetCenterPoint(level == 4 ? 1f : level == 5 ? 0.955f : HoleT);
        holePosition = center + Vector3.up * 0.015f;

        GameObject cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cup.name = "Hole";
        cup.transform.SetParent(runtimeRoot.transform);
        cup.transform.position = holePosition + Vector3.up * 0.002f;
        cup.transform.localScale = new Vector3(holeRadius * 1.82f, 0.012f, holeRadius * 1.82f);

        Renderer cupRenderer = cup.GetComponent<Renderer>();
        if (cupRenderer != null)
            cupRenderer.sharedMaterial = darkMaterial;

        DisableAndDestroyCollider(cup);
        BuildCupLip();

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Flag Pole";
        pole.transform.SetParent(runtimeRoot.transform);
        pole.transform.position = holePosition + Vector3.up * 1.45f;
        pole.transform.localScale = new Vector3(0.035f, 1.45f, 0.035f);

        Renderer poleRenderer = pole.GetComponent<Renderer>();
        if (poleRenderer != null)
            poleRenderer.sharedMaterial = curbMaterial;

        DisableAndDestroyCollider(pole);
        flagPoleTransform = pole.transform;
        flagPoleBaseLocalPosition = flagPoleTransform.localPosition;

        GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
        flag.name = "Flag";
        flag.transform.SetParent(runtimeRoot.transform);
        flag.transform.position = holePosition + new Vector3(0.42f, 2.30f, 0f);
        flag.transform.localScale = new Vector3(0.80f, 0.42f, 0.035f);

        Renderer flagRenderer = flag.GetComponent<Renderer>();
        if (flagRenderer != null)
            flagRenderer.sharedMaterial = flagMaterial;

        DisableAndDestroyCollider(flag);
        flagBannerTransform = flag.transform;
        flagBannerBaseLocalPosition = flagBannerTransform.localPosition;
    }

    private void BuildCupLip()
    {
        // The torus is mostly recessed into the green. Its top is flush
        // with the putting surface so a rolling ball can enter the cup.
        const int around = 48, across = 10;
        float major = holeRadius + 0.075f;
        float tube = 0.055f;
        Vector3[] vertices = new Vector3[(around + 1) * (across + 1)];
        int[] triangles = new int[around * across * 6];
        for (int a = 0; a <= around; a++)
        {
            float theta = a * Mathf.PI * 2f / around;
            Vector3 outward = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
            for (int b = 0; b <= across; b++)
            {
                float phi = b * Mathf.PI * 2f / across;
                vertices[a * (across + 1) + b] =
                    outward * (major + tube * Mathf.Cos(phi)) +
                    Vector3.up * (-0.055f + tube * Mathf.Sin(phi));
                if (a == around || b == across) continue;
                int v = a * (across + 1) + b;
                int k = (a * across + b) * 6;
                triangles[k] = v;
                triangles[k + 1] = v + 1;
                triangles[k + 2] = v + across + 1;
                triangles[k + 3] = v + 1;
                triangles[k + 4] = v + across + 2;
                triangles[k + 5] = v + across + 1;
            }
        }
        Mesh mesh = new Mesh();
        mesh.name = "Flush Cup Rim";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GameObject lip = new GameObject("Flush Hole Lip");
        lip.transform.SetParent(runtimeRoot.transform);
        lip.transform.position = holePosition;
        lip.AddComponent<MeshFilter>().sharedMesh = mesh;
        lip.AddComponent<MeshRenderer>().sharedMaterial = goldMetalMaterial;
        MeshCollider collider = lip.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.material = greenPhysics;
    }

    private void BuildBall()
    {
        Vector3 startCenter = GetCenterPoint(TeeT);
        ballStartPosition = startCenter + Vector3.up * (ballRadius + 0.035f);
        lastLie = ballStartPosition;

        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Golf Ball";
        ball.transform.SetParent(runtimeRoot.transform);
        ball.transform.position = ballStartPosition;
        ball.transform.localScale = Vector3.one * (ballRadius * 2f);

        Renderer renderer = ball.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = ballMaterial;

        Collider ballCollider = ball.GetComponent<Collider>();
        if (ballCollider != null)
            ballCollider.material = ballPhysics;

        ballRb = ball.AddComponent<Rigidbody>();
        ballRb.mass = 1f;
        ballRb.useGravity = true;
        ballRb.linearDamping = ballLinearDamping;
        ballRb.angularDamping = ballAngularDamping;
        ballRb.interpolation = RigidbodyInterpolation.Interpolate;
        ballRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballRb.maxAngularVelocity = 50f;
        ballRb.solverIterations = 10;
        ballRb.solverVelocityIterations = 10;
        ball.AddComponent<MiniGolfBallSound>().owner = this;
    }

    private void BuildPlayer()
    {
        GameObject root = new GameObject("Golfer");
        root.transform.SetParent(runtimeRoot.transform);
        playerRoot = root.transform;

        GameObject hips = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        hips.name = "Body";
        hips.transform.SetParent(playerRoot);
        hips.transform.localPosition = new Vector3(0f, 0.94f, 0f);
        hips.transform.localScale = new Vector3(0.68f, 0.95f, 0.68f);
        hips.GetComponent<Renderer>().sharedMaterial = yellowSuitMaterial;
        DisableAndDestroyCollider(hips);

        GameObject leftLeg = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        leftLeg.name = "Left Leg";
        leftLeg.transform.SetParent(playerRoot);
        leftLeg.transform.localPosition = new Vector3(-0.14f, 0.46f, 0.00f);
        leftLeg.transform.localScale = new Vector3(0.23f, 0.58f, 0.23f);
        leftLeg.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        leftLeg.GetComponent<Renderer>().sharedMaterial = yellowSuitMaterial;
        DisableAndDestroyCollider(leftLeg);

        GameObject rightLeg = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        rightLeg.name = "Right Leg";
        rightLeg.transform.SetParent(playerRoot);
        rightLeg.transform.localPosition = new Vector3(0.18f, 0.46f, 0.08f);
        rightLeg.transform.localScale = new Vector3(0.23f, 0.62f, 0.23f);
        rightLeg.transform.localRotation = Quaternion.Euler(0f, 0f, -14f);
        rightLeg.GetComponent<Renderer>().sharedMaterial = yellowSuitMaterial;
        DisableAndDestroyCollider(rightLeg);

        GameObject leftArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        leftArm.name = "Left Arm";
        leftArm.transform.SetParent(playerRoot);
        leftArm.transform.localPosition = new Vector3(-0.48f, 1.13f, 0.00f);
        leftArm.transform.localScale = new Vector3(0.16f, 0.50f, 0.16f);
        leftArm.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
        leftArm.GetComponent<Renderer>().sharedMaterial = yellowSuitMaterial;
        DisableAndDestroyCollider(leftArm);

        GameObject rightArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        rightArm.name = "Right Arm";
        rightArm.transform.SetParent(playerRoot);
        rightArm.transform.localPosition = new Vector3(0.43f, 1.10f, 0.10f);
        rightArm.transform.localScale = new Vector3(0.16f, 0.48f, 0.16f);
        rightArm.transform.localRotation = Quaternion.Euler(14f, 0f, -34f);
        rightArm.GetComponent<Renderer>().sharedMaterial = yellowSuitMaterial;
        DisableAndDestroyCollider(rightArm);

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(playerRoot);
        head.transform.localPosition = new Vector3(0.02f, 1.88f, 0f);
        head.transform.localScale = new Vector3(0.43f, 0.50f, 0.40f);
        head.GetComponent<Renderer>().sharedMaterial = greenFaceMaterial;
        DisableAndDestroyCollider(head);

        GameObject hatBrim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hatBrim.name = "Hat Brim";
        hatBrim.transform.SetParent(playerRoot);
        hatBrim.transform.localPosition = new Vector3(0.04f, 2.07f, 0f);
        hatBrim.transform.localScale = new Vector3(0.38f, 0.03f, 0.38f);
        hatBrim.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
        hatBrim.GetComponent<Renderer>().sharedMaterial = yellowSuitMaterial;
        DisableAndDestroyCollider(hatBrim);

        GameObject hatTop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hatTop.name = "Hat Top";
        hatTop.transform.SetParent(playerRoot);
        hatTop.transform.localPosition = new Vector3(0.06f, 2.22f, 0f);
        hatTop.transform.localScale = new Vector3(0.22f, 0.16f, 0.22f);
        hatTop.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
        hatTop.GetComponent<Renderer>().sharedMaterial = yellowSuitMaterial;
        DisableAndDestroyCollider(hatTop);

        GameObject hatBand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hatBand.name = "Hat Band";
        hatBand.transform.SetParent(playerRoot);
        hatBand.transform.localPosition = new Vector3(0.06f, 2.14f, 0f);
        hatBand.transform.localScale = new Vector3(0.225f, 0.02f, 0.225f);
        hatBand.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
        hatBand.GetComponent<Renderer>().sharedMaterial = hatBandMaterial;
        DisableAndDestroyCollider(hatBand);

        GameObject feather = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        feather.name = "Feather";
        feather.transform.SetParent(playerRoot);
        feather.transform.localPosition = new Vector3(0.22f, 2.39f, 0.04f);
        feather.transform.localScale = new Vector3(0.05f, 0.20f, 0.05f);
        feather.transform.localRotation = Quaternion.Euler(20f, 8f, -28f);
        feather.GetComponent<Renderer>().sharedMaterial = whiteMaterial;
        DisableAndDestroyCollider(feather);

        GameObject leftShoe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftShoe.name = "Left Shoe";
        leftShoe.transform.SetParent(playerRoot);
        leftShoe.transform.localPosition = new Vector3(-0.18f, 0.06f, 0.12f);
        leftShoe.transform.localScale = new Vector3(0.26f, 0.10f, 0.48f);
        leftShoe.GetComponent<Renderer>().sharedMaterial = shoeMaterial;
        DisableAndDestroyCollider(leftShoe);

        GameObject rightShoe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightShoe.name = "Right Shoe";
        rightShoe.transform.SetParent(playerRoot);
        rightShoe.transform.localPosition = new Vector3(0.28f, 0.06f, 0.22f);
        rightShoe.transform.localScale = new Vector3(0.26f, 0.10f, 0.48f);
        rightShoe.transform.localRotation = Quaternion.Euler(0f, 16f, 0f);
        rightShoe.GetComponent<Renderer>().sharedMaterial = shoeMaterial;
        DisableAndDestroyCollider(rightShoe);

        GameObject shirt = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shirt.name = "Shirt";
        shirt.transform.SetParent(playerRoot);
        shirt.transform.localPosition = new Vector3(0f, 1.02f, 0.26f);
        shirt.transform.localScale = new Vector3(0.18f, 0.65f, 0.08f);
        shirt.GetComponent<Renderer>().sharedMaterial = whiteMaterial;
        DisableAndDestroyCollider(shirt);

        GameObject tie = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tie.name = "Tie";
        tie.transform.SetParent(playerRoot);
        tie.transform.localPosition = new Vector3(0f, 1.04f, 0.31f);
        tie.transform.localScale = new Vector3(0.06f, 0.55f, 0.05f);
        tie.transform.localRotation = Quaternion.Euler(0f, 0f, 6f);
        tie.GetComponent<Renderer>().sharedMaterial = darkMaterial;
        DisableAndDestroyCollider(tie);

        GameObject pivot = new GameObject("Club Pivot");
        pivot.transform.SetParent(playerRoot);
        pivot.transform.localPosition = new Vector3(0.30f, 1.04f, 0.22f);
        pivot.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
        clubPivot = pivot.transform;

        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "Putter Shaft";
        shaft.transform.SetParent(clubPivot);
        shaft.transform.localPosition = new Vector3(0f, -0.06f, 0.60f);
        shaft.transform.localScale = new Vector3(0.028f, 0.72f, 0.028f);
        shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shaft.GetComponent<Renderer>().sharedMaterial = darkMaterial;
        DisableAndDestroyCollider(shaft);

        GameObject hosel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hosel.name = "Putter Hosel";
        hosel.transform.SetParent(clubPivot);
        hosel.transform.localPosition = new Vector3(0.06f, -0.11f, 1.16f);
        hosel.transform.localScale = new Vector3(0.05f, 0.14f, 0.05f);
        hosel.transform.localRotation = Quaternion.Euler(0f, 0f, -26f);
        hosel.GetComponent<Renderer>().sharedMaterial = goldMetalMaterial;
        DisableAndDestroyCollider(hosel);

        GameObject clubHead = GameObject.CreatePrimitive(PrimitiveType.Cube);
        clubHead.name = "Putter Head";
        clubHead.transform.SetParent(clubPivot);
        clubHead.transform.localPosition = new Vector3(0f, -0.16f, 1.30f);
        clubHead.transform.localScale = new Vector3(0.42f, 0.10f, 0.18f);
        clubHead.GetComponent<Renderer>().sharedMaterial = goldMetalMaterial;
        DisableAndDestroyCollider(clubHead);

        GameObject faceInsert = GameObject.CreatePrimitive(PrimitiveType.Cube);
        faceInsert.name = "Putter Face Insert";
        faceInsert.transform.SetParent(clubHead.transform);
        faceInsert.transform.localPosition = new Vector3(0f, 0.0f, 0.055f);
        faceInsert.transform.localScale = new Vector3(0.24f, 0.065f, 0.03f);
        faceInsert.GetComponent<Renderer>().sharedMaterial = putterFaceMaterial;
        DisableAndDestroyCollider(faceInsert);
    }

    private void BuildAimArrow()
    {
        GameObject arrowRootObject = new GameObject("Aim Arrow");
        arrowRootObject.transform.SetParent(runtimeRoot.transform);
        aimArrowRoot = arrowRootObject.transform;

        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shaft.name = "Arrow Shaft";
        shaft.transform.SetParent(aimArrowRoot);
        shaft.GetComponent<Renderer>().sharedMaterial = arrowMaterial;
        DisableAndDestroyCollider(shaft);
        aimArrowShaft = shaft.transform;

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        head.name = "Arrow Head";
        head.transform.SetParent(aimArrowRoot);
        head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        head.GetComponent<Renderer>().sharedMaterial = arrowMaterial;
        DisableAndDestroyCollider(head);
        aimArrowHead = head.transform;
    }

    private void DisableAndDestroyCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
    }

    private void OnGUI()
    {
        if (guiPixel == null)
        {
            guiPixel = new Texture2D(1, 1);
            guiPixel.name = "MiniGolf GUI Pixel";
            guiPixel.SetPixel(0, 0, Color.white);
            guiPixel.Apply();
        }

        if (menuPage != MenuPage.Playing)
        {
            DrawMenu();
            return;
        }
        if (GUI.Button(new Rect(20f, 70f, 120f, 42f), "MENU / PAUSE"))
            SetMenu(MenuPage.Paused);
        DrawInstructions();
        DrawPowerMeter();
        DrawStrokeCounter();
        DrawAimArrowPowerLabel();

        if (strokePopupTimer > 0f)
            DrawStrokePopup();

        if (state == GameState.Holed || state == GameState.OutOfStrokes)
            DrawHoleComplete();
    }

    private void DrawInstructions()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = Mathf.Clamp(Screen.height / 38, 18, 28);
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;

        Rect shadow = new Rect(22f, 20f, Screen.width - 44f, 42f);
        Rect text = new Rect(20f, 18f, Screen.width - 44f, 42f);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(shadow, "A / D = Aim     SPACE = Power/Hit     F = Hole View     Mouse Drag = Camera     H = Reset Camera     R = Restart     ESC = Menu", style);

        GUI.color = Color.white;
        GUI.Label(text, "A / D = Aim     SPACE = Power/Hit     F = Hole View     Mouse Drag = Camera     H = Reset Camera     R = Restart     ESC = Menu", style);
    }

    private void DrawPowerMeter()
    {
        float width = Mathf.Min(Screen.width * 0.52f, 720f);
        float height = Mathf.Clamp(Screen.height * 0.035f, 26f, 42f);
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height - height - 34f;

        Rect outer = new Rect(x, y, width, height);

        GUI.color = new Color(0f, 0f, 0f, 0.80f);
        GUI.DrawTexture(outer, guiPixel);

        float padding = 4f;
        Rect inner = new Rect(
            x + padding,
            y + padding,
            Mathf.Max(0f, (width - padding * 2f) * Mathf.Clamp01(power01)),
            height - padding * 2f
        );

        GUI.color = Color.Lerp(new Color(0.15f, 0.85f, 0.18f), new Color(0.95f, 0.12f, 0.08f), power01);
        GUI.DrawTexture(inner, guiPixel);

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.fontSize = Mathf.Clamp(Screen.height / 42, 17, 25);
        labelStyle.normal.textColor = Color.white;

        GUI.color = Color.white;
        GUI.Label(outer, "POWER  " + Mathf.RoundToInt(Mathf.Clamp01(power01) * 100f) + "%", labelStyle);
    }

    private void DrawStrokeCounter()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = Mathf.Clamp(Screen.height / 32, 20, 34);
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.UpperRight;
        style.normal.textColor = Color.white;

        style.fontSize = Mathf.Clamp(Screen.height / 48, 20, 28);
        style.alignment = TextAnchor.MiddleRight;
        GUI.color = new Color(0f, 0f, 0f, 0.68f);
        GUI.DrawTexture(new Rect(Screen.width - 365f, 8f, 345f, 78f), guiPixel);
        GUI.color = Color.white;
        GUI.Label(new Rect(Screen.width - 355f, 7f, 330f, 42f),
            "STROKES: " + strokes + " / " + StrokeLimit, style);
        GUI.Label(new Rect(Screen.width - 355f, 45f, 330f, 38f),
            "LEVEL " + level + " / 5", style);
    }

    private void DrawAimArrowPowerLabel()
    {
        if (gameCamera == null || aimArrowRoot == null)
            return;

        if (!(state == GameState.Aiming || state == GameState.Swinging))
            return;

        Vector3 screen = gameCamera.WorldToScreenPoint(arrowTipWorld + Vector3.up * 0.20f);
        if (screen.z <= 0f)
            return;

        float guiX = screen.x - 70f;
        float guiY = Screen.height - screen.y - 50f;

        Rect bg = new Rect(guiX, guiY, 140f, 34f);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(bg, guiPixel);

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontStyle = FontStyle.Bold;
        style.fontSize = 18;
        style.normal.textColor = Color.white;

        GUI.color = Color.white;
        GUI.Label(bg, "PWR " + Mathf.RoundToInt(Mathf.Clamp01(power01) * 100f) + "%", style);
    }

    private void DrawStrokePopup()
    {
        int fontSize = Mathf.Clamp(Screen.height / 7, 70, 145);

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;

        Rect center = new Rect(0f, Screen.height * 0.22f, Screen.width, fontSize + 30f);
        Rect shadow = new Rect(5f, Screen.height * 0.22f + 5f, Screen.width, fontSize + 30f);

        style.normal.textColor = new Color(0f, 0f, 0f, 0.72f);
        GUI.Label(shadow, strokes.ToString(), style);

        style.normal.textColor = Color.white;
        GUI.Label(center, strokes.ToString(), style);
    }

    private void DrawHoleComplete()
    {
        float width = Mathf.Min(Screen.width * 0.58f, 760f);
        float height = 220f;
        Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.26f, width, height);

        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(panel, guiPixel);

        GUIStyle title = new GUIStyle(GUI.skin.label);
        title.alignment = TextAnchor.MiddleCenter;
        title.fontStyle = FontStyle.Bold;
        title.fontSize = Mathf.Clamp(Screen.height / 25, 30, 52);
        title.normal.textColor = Color.white;

        GUIStyle subtitle = new GUIStyle(title);
        subtitle.fontSize = Mathf.Clamp(Screen.height / 46, 18, 28);

        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x, panel.y + 10f, panel.width, 76f),
            state == GameState.OutOfStrokes ? "SHOT LIMIT REACHED" : qualified ? "HOLE COMPLETE!" : "IN THE HOLE", title);
        GUI.Label(new Rect(panel.x, panel.y + 90f, panel.width, 48f),
            strokes + " / " + StrokeLimit + " strokes" +
            (qualified ? (level < 5 ? "  -  Next level unlocked!" : "  -  All levels complete!") :
                "  -  Replay to qualify"), subtitle);
        GUIStyle button = MenuButtonStyle();
        if (GUI.Button(new Rect(panel.x + 20f, panel.y + 150f, panel.width * 0.45f - 25f, 58f), "REPLAY", button))
            LoadLevel(level);
        if (GUI.Button(new Rect(panel.x + panel.width * 0.5f, panel.y + 150f, panel.width * 0.45f, 58f),
            qualified && level < 5 ? "NEXT LEVEL" : "LEVEL SELECT", button))
        {
            if (qualified && level < 5) LoadLevel(level + 1);
            else SetMenu(MenuPage.Levels);
        }
    }

    private GUIStyle MenuButtonStyle()
    {
        GUIStyle style = new GUIStyle(GUI.skin.button);
        style.fontSize = Mathf.Clamp(Screen.height / 55, 18, 25);
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.clipping = TextClipping.Overflow;
        style.padding = new RectOffset(12, 12, 10, 10);
        return style;
    }

    private void DrawMenu()
    {
        GUI.color = new Color(0.03f, 0.10f, 0.08f, 0.93f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), guiPixel);
        GUI.color = Color.white;
        float width = Mathf.Min(510f, Screen.width - 40f);
        float x = (Screen.width - width) * 0.5f;
        float y = Mathf.Max(15f, Screen.height * 0.11f);
        GUIStyle button = MenuButtonStyle();
        GUIStyle heading = new GUIStyle(GUI.skin.label);
        heading.alignment = TextAnchor.MiddleCenter;
        heading.fontStyle = FontStyle.Bold;
        heading.fontSize = Mathf.Clamp(Screen.height / 28, 28, 52);
        heading.clipping = TextClipping.Overflow;
        heading.padding = new RectOffset(6, 6, 12, 12);
        heading.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, width, 100f), "GOOFY MINI GOLF", heading);
        float row = y + 105f;
        if (menuPage == MenuPage.Title || menuPage == MenuPage.Paused)
        {
            if (GUI.Button(new Rect(x, row, width, 64f), menuPage == MenuPage.Paused ? "RESUME" : "PLAY", button))
                SetMenu(MenuPage.Playing);
            if (GUI.Button(new Rect(x, row + 72f, width, 64f), "HOW TO PLAY", button)) SetMenu(MenuPage.HowTo);
            if (GUI.Button(new Rect(x, row + 144f, width, 64f), "LEVEL SELECT", button)) SetMenu(MenuPage.Levels);
            if (GUI.Button(new Rect(x, row + 216f, width, 64f), muted ? "UNMUTE" : "MUTE", button))
            {
                muted = !muted;
                if (musicSource != null) musicSource.mute = muted;
                PlayerPrefs.SetInt("MiniGolfMuted", muted ? 1 : 0);
                PlayerPrefs.Save();
            }
            if (menuPage == MenuPage.Paused && GUI.Button(new Rect(x, row + 288f, width, 64f), "TITLE", button))
                SetMenu(MenuPage.Title);
        }
        else if (menuPage == MenuPage.HowTo)
        {
            GUIStyle body = new GUIStyle(GUI.skin.label);
            body.alignment = TextAnchor.UpperCenter;
            body.wordWrap = true;
            body.fontSize = Mathf.Clamp(Screen.height / 55, 16, 25);
            body.clipping = TextClipping.Overflow;
            body.padding = new RectOffset(10, 10, 10, 10);
            body.normal.textColor = Color.white;
            GUI.Label(new Rect(x, row, width, 270f),
                "A / D: aim around the ball\nHold SPACE: charge power; release to hit\nF: look toward the hole  |  Drag mouse: orbit camera\nH: reset camera  |  R: restart hole\nESC or MENU / PAUSE: open menu\nFinish within the stroke limit to unlock the next level.", body);
            if (GUI.Button(new Rect(x, row + 285f, width, 64f), "BACK", button)) SetMenu(MenuPage.Title);
        }
        else if (menuPage == MenuPage.Levels)
        {
            for (int i = 1; i <= 5; i++)
            {
                GUI.enabled = i <= unlockedLevel;
                int selected = i;
                if (GUI.Button(new Rect(x, row + (i - 1) * 68f, width, 60f),
                    "LEVEL " + i + "   (" + StrokeLimits[i - 1] + " strokes)" +
                    (i > unlockedLevel ? "  -  LOCKED" : i == level ? "  -  CURRENT" : ""), button))
                {
                    LoadLevel(selected);
                    SetMenu(MenuPage.Playing);
                }
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(x, row + 348f, width, 60f), "BACK", button)) SetMenu(MenuPage.Title);
        }
    }

    private AudioClip MakeClubImpact()
    {
        const int rate = 22050;
        const float duration = 0.19f;
        int count = Mathf.RoundToInt(rate * duration);
        float[] samples = new float[count];
        System.Random noise = new System.Random(4921);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            float crack = ((float)noise.NextDouble() * 2f - 1f) *
                Mathf.Exp(-t * 95f) * 0.35f;
            float wood = Mathf.Sin(2f * Mathf.PI * (410f * t - 560f * t * t)) *
                Mathf.Exp(-t * 25f) * 0.45f;
            float low = Mathf.Sin(2f * Mathf.PI * 145f * t) *
                Mathf.Exp(-t * 36f) * 0.23f;
            samples[i] = Mathf.Clamp(crack + wood + low, -0.85f, 0.85f);
        }
        AudioClip clip = AudioClip.Create("Golf club impact", count, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip MakeBackgroundMusic()
    {
        const int rate = 22050;
        const int duration = 8;
        float[] samples = new float[rate * duration];
        float[] bass = { 110f, 130.81f, 146.83f, 98f };
        float[] melody = { 329.63f, 392f, 440f, 392f,
            349.23f, 440f, 392f, 329.63f,
            293.66f, 369.99f, 440f, 369.99f,
            293.66f, 329.63f, 392f, 329.63f };
        System.Random hats = new System.Random(173);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            int beat = Mathf.FloorToInt(t * 2f);
            float beatPhase = t * 2f - beat;
            float measurePhase = t % 2f;
            float bassHz = bass[Mathf.FloorToInt(t * 0.5f) % bass.Length];
            float bassNote = Mathf.Sin(2f * Mathf.PI * bassHz * t) *
                (0.35f + 0.65f * Mathf.Exp(-measurePhase * 3f));
            float leadHz = melody[beat % melody.Length];
            float lead = (Mathf.Sin(2f * Mathf.PI * leadHz * t) +
                0.15f * Mathf.Sin(2f * Mathf.PI * leadHz * 2f * t)) *
                Mathf.Sin(Mathf.PI * Mathf.Clamp01(beatPhase)) * 0.31f;
            float kick = Mathf.Sin(2f * Mathf.PI * (75f - 33f * beatPhase) *
                beatPhase * 0.5f) * Mathf.Exp(-beatPhase * 20f) * 0.22f;
            float hat = ((float)hats.NextDouble() * 2f - 1f) *
                Mathf.Exp(-beatPhase * 45f) * 0.035f;
            samples[i] = Mathf.Clamp((bassNote * 0.28f + lead + kick + hat) * 0.7f,
                -0.7f, 0.7f);
        }
        // Match the endpoints so the eight-second loop has no click.
        for (int i = 0; i < 256; i++)
        {
            float blend = i / 256f;
            samples[samples.Length - 256 + i] = Mathf.Lerp(
                samples[samples.Length - 256 + i], samples[i], blend);
        }
        AudioClip clip = AudioClip.Create("Mini golf background loop", samples.Length,
            1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip MakeSound(string name, bool hit)
    {
        const int rate = 22050;
        int count = hit ? rate / 3 : rate / 5;
        float[] samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            float p = i / (float)count;
            float phase = 2f * Mathf.PI * (hit ? (720f * t - 565f * t * t / (2f * (count / (float)rate))) :
                (290f * t + 290f * t * t / (2f * (count / (float)rate))));
            float wave = Mathf.Sin(phase) + 0.28f * Mathf.Sin(phase * (hit ? 1.5f : 2.1f));
            samples[i] = wave * Mathf.Pow(1f - p, 2f) * 0.45f;
        }
        AudioClip clip = AudioClip.Create(name, count, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip MakeTrumpet()
    {
        const int rate = 22050;
        const float duration = 1.65f;
        float[] samples = new float[(int)(rate * duration)];
        float[] notes = { 392f, 523.25f, 659.25f, 784f };
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            int n = Mathf.Min((int)(t / 0.34f), 3);
            float local = t - n * 0.34f;
            float envelope = Mathf.Min(1f, local * 35f) * Mathf.Clamp01((duration - t) * 4f);
            float phase = 2f * Mathf.PI * notes[n] * t;
            samples[i] = (Mathf.Sin(phase) + 0.35f * Mathf.Sin(phase * 2f) +
                0.18f * Mathf.Sin(phase * 3f)) * envelope * 0.25f;
        }
        AudioClip clip = AudioClip.Create("Victory trumpet", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip MakeGlassCrash()
    {
        const int rate = 22050;
        float[] samples = new float[rate / 3];
        System.Random random = new System.Random(318);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            float fade = Mathf.Pow(1f - i / (float)samples.Length, 3f);
            samples[i] = ((float)random.NextDouble() * 2f - 1f) * fade * 0.32f +
                Mathf.Sin(2f * Mathf.PI * 1350f * t) * fade * 0.16f;
        }
        AudioClip clip = AudioClip.Create("Glass shatter", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void SpawnConfetti()
    {
        GameObject objectRoot = new GameObject("Victory Confetti");
        objectRoot.transform.SetParent(runtimeRoot.transform);
        objectRoot.transform.position = holePosition + Vector3.up * 0.45f;
        ParticleSystem confetti = objectRoot.AddComponent<ParticleSystem>();
        confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = confetti.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.7f, 3.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
        main.gravityModifier = 0.75f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.3f, 0.2f), new Color(0.2f, 0.85f, 1f));
        var emission = confetti.emission;
        emission.enabled = false;
        var shape = confetti.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 0.25f;
        ParticleSystemRenderer particles = objectRoot.GetComponent<ParticleSystemRenderer>();
        particles.sharedMaterial = confettiMaterial;
        confetti.Play();
        confetti.Emit(450);
        Destroy(objectRoot, 4f);
    }

    private void PlaySound(AudioClip clip, float volume)
    {
        if (!muted && soundSource != null && clip != null)
            soundSource.PlayOneShot(clip, volume);
    }

    public void OnBallCollision(Collision collision)
    {
        MiniGolfGlass glass = collision.collider.GetComponent<MiniGolfGlass>();
        if (glass != null && collision.contactCount > 0 &&
            collision.relativeVelocity.magnitude > glass.breakThreshold)
        {
            BreakGlass(glass.gameObject, collision.GetContact(0), collision.relativeVelocity.magnitude);
            return;
        }
        if (menuPage != MenuPage.Playing || Time.time - lastBounceTime < 0.09f) return;
        if (collision.relativeVelocity.magnitude < 1.5f) return;
        string name = collision.collider.name;
        if (!name.Contains("Curb") && !name.Contains("bumper") && !name.Contains("gate") &&
            !name.Contains("shortcut") && !name.Contains("End Curb") && !name.Contains("Start Curb")) return;
        lastBounceTime = Time.time;
        PlaySound(bounceClip, Mathf.Clamp(collision.relativeVelocity.magnitude / 13f, 0.17f, 0.8f));
    }

    private bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape)) return true;
#endif
        return false;
    }

    private bool LeftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.aKey.isPressed)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.A))
            return true;
#endif
        return false;
    }

    private bool RightHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.dKey.isPressed)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.D))
            return true;
#endif
        return false;
    }

    private bool SpaceDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Space))
            return true;
#endif
        return false;
    }

    private bool SpaceHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.Space))
            return true;
#endif
        return false;
    }

    private bool SpaceUp()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasReleasedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyUp(KeyCode.Space))
            return true;
#endif
        return false;
    }

    private bool HolePreviewPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.F))
            return true;
#endif
        return false;
    }

    private bool ResetCameraPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.H))
            return true;
#endif
        return false;
    }

    private bool MouseLeftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(0))
            return true;
#endif
        return false;
    }

    private Vector2 MouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.delta.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Mouse X") * 12f, Input.GetAxisRaw("Mouse Y") * 12f);
#else
        return Vector2.zero;
#endif
    }

    private bool RestartPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.R))
            return true;
#endif
        return false;
    }

    private void OnDestroy()
    {
        if (guiPixel != null)
            Destroy(guiPixel);

        if (greenMaterial != null) Destroy(greenMaterial);
        if (curbMaterial != null) Destroy(curbMaterial);
        if (ballMaterial != null) Destroy(ballMaterial);
        if (yellowSuitMaterial != null) Destroy(yellowSuitMaterial);
        if (greenFaceMaterial != null) Destroy(greenFaceMaterial);
        if (darkMaterial != null) Destroy(darkMaterial);
        if (roughMaterial != null) Destroy(roughMaterial);
        if (flagMaterial != null) Destroy(flagMaterial);
        if (hatBandMaterial != null) Destroy(hatBandMaterial);
        if (arrowMaterial != null) Destroy(arrowMaterial);
        if (shoeMaterial != null) Destroy(shoeMaterial);
        if (whiteMaterial != null) Destroy(whiteMaterial);
        if (goldMetalMaterial != null) Destroy(goldMetalMaterial);
        if (putterFaceMaterial != null) Destroy(putterFaceMaterial);
        if (woodMaterial != null) Destroy(woodMaterial);
        if (windmillWhiteMaterial != null) Destroy(windmillWhiteMaterial);
        if (windmillRedMaterial != null) Destroy(windmillRedMaterial);
        if (windmillBlueMaterial != null) Destroy(windmillBlueMaterial);
        if (accentMaterial != null) Destroy(accentMaterial);
        if (glassMaterial != null) Destroy(glassMaterial);
        if (confettiMaterial != null) Destroy(confettiMaterial);
        if (hitClip != null) Destroy(hitClip);
        if (generatedMusic != null) Destroy(generatedMusic);
        if (bounceClip != null) Destroy(bounceClip);
        if (trumpetClip != null) Destroy(trumpetClip);
        if (glassClip != null) Destroy(glassClip);
        Time.timeScale = 1f;

        if (curbPhysics != null) Destroy(curbPhysics);
        if (greenPhysics != null) Destroy(greenPhysics);
        if (ballPhysics != null) Destroy(ballPhysics);
    }
}

public class MiniGolfBallSound : MonoBehaviour
{
    public MiniGolfGame owner;
    private void OnCollisionEnter(Collision collision)
    {
        if (owner != null) owner.OnBallCollision(collision);
    }
}

public class MiniGolfGlass : MonoBehaviour
{
    public MiniGolfGame owner;
    public float breakThreshold = 2.2f;
}

public class MiniGolfWindmill : MonoBehaviour
{
    public float degreesPerSecond = 100f;
    private Rigidbody rotor;

    private void Awake()
    {
        rotor = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (rotor != null)
            rotor.MoveRotation(rotor.rotation * Quaternion.Euler(0f, 0f,
                degreesPerSecond * Time.fixedDeltaTime));
    }
}

public class MiniGolfSweeper : MonoBehaviour
{
    public float degreesPerSecond = 90f;
    private Rigidbody body;
    private void Awake() { body = GetComponent<Rigidbody>(); }
    private void FixedUpdate()
    {
        if (body != null)
            body.MoveRotation(body.rotation * Quaternion.Euler(0f,
                degreesPerSecond * Time.fixedDeltaTime, 0f));
    }
}

public class MiniGolfWater : MonoBehaviour
{
    public MiniGolfGame owner;
    private void OnTriggerEnter(Collider other)
    {
        if (owner != null && other.name == "Golf Ball") owner.EnterLevelFiveWater();
    }
}
