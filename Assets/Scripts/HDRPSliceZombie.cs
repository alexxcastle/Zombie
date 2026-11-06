using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public sealed class HDRPSliceZombie : MonoBehaviour
{
    [SerializeField] float health = 102f;
    [SerializeField] float approachSpeed = 0.68f;
    [SerializeField] float attackRange = 1.55f;
    [SerializeField] float attackDamage = 9f;
    [SerializeField] float attackInterval = 1.2f;
    [SerializeField] Transform player;
    [SerializeField] HDRPSlicePlayer playerHealth;
    Renderer[] renderers;
    Color[] originalColors;
    float flashUntil;
    float nextAttack;
    bool dead;

    public void Initialize(Transform playerRoot, HDRPSlicePlayer playerController)
    {
        player = playerRoot;
        playerHealth = playerController;
    }

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var mat = renderers[i] != null ? renderers[i].material : null;
            originalColors[i] = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
        }
    }

    public bool TakeDamage(float amount)
    {
        if (dead) return false;
        health -= amount;
        flashUntil = Time.time + 0.09f;
        if (health <= 0f)
        {
            dead = true;
            foreach (var animator in GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            gameObject.SetActive(false);
            return true;
        }
        return false;
    }

    void Update()
    {
        if (dead) return;
        bool flash = Time.time < flashUntil;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            var mat = renderers[i].material;
            if (mat != null && mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", flash ? Color.white : originalColors[i]);
        }
        if (player == null || playerHealth == null || playerHealth.IsDead) return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance < 26f && distance > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toPlayer, Vector3.up), 2.2f * Time.deltaTime);
        if (distance < 26f && distance > attackRange)
        {
            Vector3 direction = toPlayer / Mathf.Max(0.001f, distance);
            transform.position += direction * approachSpeed * Time.deltaTime;
        }
        else if (distance <= attackRange && Time.time >= nextAttack)
        {
            nextAttack = Time.time + attackInterval;
            playerHealth.TakeDamage(attackDamage);
        }
    }
}
