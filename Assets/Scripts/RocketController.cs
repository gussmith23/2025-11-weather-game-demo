using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Weather2D))]
public class RocketController : MonoBehaviour
{
    [SerializeField] private Weather2D weather;
    [SerializeField] private Transform rocketVisual;
    [SerializeField] private bool autoLaunch = true;
    [SerializeField] private bool createVisualIfMissing = true;
    [SerializeField] private Vector2 visualScale = new Vector2(4.5f, 4.5f);
    [SerializeField] private float defaultBurstDuration = 0.25f;
    [SerializeField] private float defaultBurstInterval = 0.18f;
    [SerializeField] private float visualGroundOffset = -2.25f;

    private Coroutine _launchRoutine;

    private void Reset()
    {
        weather = GetComponent<Weather2D>();
    }

    private void Awake()
    {
        if (weather == null)
        {
            weather = GetComponent<Weather2D>();
        }
    }

    private void OnEnable()
    {
        if (weather != null)
        {
            weather.DemoApplied += HandleScenarioApplied;
        }
    }

    private void Start()
    {
        if (weather != null)
        {
            HandleScenarioApplied(weather.CurrentScenario);
        }
    }

    private void OnDisable()
    {
        if (weather != null)
        {
            weather.DemoApplied -= HandleScenarioApplied;
        }

        if (_launchRoutine != null)
        {
            StopCoroutine(_launchRoutine);
            _launchRoutine = null;
        }
    }

    public void LaunchCurrentScenario()
    {
        if (weather == null)
            return;

        HandleScenarioApplied(weather.CurrentScenario);
    }

    private void HandleScenarioApplied(Weather2D.DemoScenario scenario)
    {
        if (_launchRoutine != null)
        {
            StopCoroutine(_launchRoutine);
            _launchRoutine = null;
        }

        if (scenario.rocketBursts == null || scenario.rocketBursts.Length == 0)
        {
            HideRocketVisual();
            return;
        }

        if (!autoLaunch)
        {
            PrepareVisual(new Vector2(scenario.rocketBursts[0].position.x, 0.02f));
            return;
        }

        _launchRoutine = StartCoroutine(RunRocketSequence(scenario));
    }

    private IEnumerator RunRocketSequence(Weather2D.DemoScenario scenario)
    {
        float duration = scenario.rocketBurstDuration > 0f ? scenario.rocketBurstDuration : defaultBurstDuration;
        float interval = scenario.rocketBurstInterval > 0f ? scenario.rocketBurstInterval : defaultBurstInterval;

        weather.ClearScriptedBursts();
        if (scenario.rocketBoostDuration > 0f)
        {
            float cond = scenario.rocketCondensationMultiplier > 0f ? scenario.rocketCondensationMultiplier : 1f;
            float precip = scenario.rocketPrecipitationMultiplier > 0f ? scenario.rocketPrecipitationMultiplier : 1f;
            weather.TriggerRocketBoost(scenario.rocketBoostDuration, cond, precip);
        }
        weather.TriggerRocketSequence(scenario.rocketBursts, scenario.rocketDelay, interval, duration);

        Vector2 pad = new Vector2(scenario.rocketBursts[0].position.x, 0.02f);
        PrepareVisual(pad);

        if (scenario.rocketDelay > 0f)
        {
            float timer = 0f;
            while (timer < scenario.rocketDelay)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / scenario.rocketDelay);
                Vector2 uv = Vector2.Lerp(pad, scenario.rocketBursts[0].position, t);
                UpdateRocketVisual(uv);
                yield return null;
            }
        }

        for (int i = 0; i < scenario.rocketBursts.Length; i++)
        {
            Vector2 start = i == 0 ? scenario.rocketBursts[i].position : scenario.rocketBursts[i - 1].position;
            Vector2 end = scenario.rocketBursts[i].position;
            float elapsed = 0f;
            while (elapsed < interval)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, interval));
                Vector2 uv = Vector2.Lerp(start, end, t);
                UpdateRocketVisual(uv);
                yield return null;
            }
        }

        if (scenario.disableBaseSourceAfterRocket)
        {
            weather.SetBaseSourceActive(false);
        }

        yield return new WaitForSeconds(1f);
        HideRocketVisual();
        _launchRoutine = null;
    }

    private void PrepareVisual(Vector2 uv)
    {
        if (rocketVisual == null && createVisualIfMissing)
        {
            rocketVisual = CreateDefaultVisual();
        }

        UpdateRocketVisual(uv);
        if (rocketVisual != null)
        {
            rocketVisual.gameObject.SetActive(true);
        }
    }

    private Transform CreateDefaultVisual()
    {
        GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        capsule.name = "Rocket Visual";
        capsule.transform.SetParent(transform, false);
        capsule.transform.localScale = new Vector3(0.1f, 0.5f, 0.1f);
        var collider = capsule.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
        return capsule.transform;
    }

    private void UpdateRocketVisual(Vector2 uv)
    {
        if (rocketVisual == null)
            return;

        Vector3 local = new Vector3((uv.x - 0.5f) * visualScale.x, visualGroundOffset + uv.y * visualScale.y, -1f);
        rocketVisual.localPosition = local;
    }

    private void HideRocketVisual()
    {
        if (rocketVisual != null)
        {
            rocketVisual.gameObject.SetActive(false);
        }
    }
}
