using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using Panda;
using MLAPI;

public class NatureBossScript : NetworkBehaviour, IBoss
{
    public NatureBossAnimationController animationController;
    public PandaBehaviour behaviourTree;
    public float btUpdatesPerSecond;
    private float time_between_ticks;
    private float time_since_last_tick;

    private GameObject[] players;
    private GameObject target_object;
    private Vector2 target_position;

    public float stompAttackRange;
    public float landSlideWidth;
    public float landSlideLength;
    public float StompCooldown;
    public float LandslideCooldown;

    public float stoneThrowTiming;
    public float stoneConjuringStart;
    public float stoneConjuringDuration;

    public float stompLandingTiming;

    private float last_stomp = -99999f;
    private float last_landslide;

    private List<Action> callbacks;
    private List<(float, Action)> timedActionPrioQueue;

    private float time_of_last_attack = -10f;

    public float hitpoints;
    private HP hp;
    public bool isAlive = true;
    // Start is called before the first frame update
    void Start()
    {
        callbacks = new List<Action>();
        timedActionPrioQueue = new List<(float, Action)>();
        time_between_ticks = 1f / btUpdatesPerSecond;
        animationController = GetComponent<NatureBossAnimationController>();
        behaviourTree = GetComponent<PandaBehaviour>();
        players = GameObject.FindGameObjectsWithTag("Player"); if (players.Length == 0) { Debug.Log("FOUND NO PLAYERS"); }
        hp = GetComponent<HP>();
        hp.maxHP = hp.currentHP.Value = hitpoints;
    }

    public void addToCallbackQueue(Action callback)
    {
        callbacks.Add(callback);
    }

    public void clearCallbackQueue()
    {
        if (callbacks.Count > 0)
        {
            int num_callbacks = callbacks.Count;
            for (int i = 0; i < num_callbacks; i++)
            {
                callbacks[i]();
                callbacks.RemoveAt(0);
            }
        }
    }

    private void addDelayedAction(float time, Action callback)
    {
        if (time < Time.time)
        {
            Debug.Log("Tried to add action in the past");
        }
        else
        {
            for (int i = 0; i < timedActionPrioQueue.Count; i++)
            {
                if (timedActionPrioQueue[i].Item1 > time)
                {
                    timedActionPrioQueue.Insert(i, (time, callback));
                    return;
                }
            }
            timedActionPrioQueue.Add((time, callback));
        }
    }

    private void checkDelayedActions()
    {
        float current_time = Time.time;
        while(timedActionPrioQueue.Count > 0 && timedActionPrioQueue[0].Item1 < current_time)
        {
            timedActionPrioQueue[0].Item2(); // Call stored function
            timedActionPrioQueue.RemoveAt(0); // Remove from queue
        }
    }

    private void die()
    {
        animationController.die();
        isAlive = false;
        foreach(GameObject player in GameObject.FindGameObjectsWithTag("Player"))
        {
            player.GetComponent<CharacterController>().Resurrect();
        }
        //gameObject.SetActive(false);
    }

    public void damage(float damage)
    {
        if (!isAlive) { return; }
        hp.ReduceHpServerRpc(damage);
        if (hp.currentHP.Value <= 0)
        {
            die();
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (isAlive && hp.currentHP.Value <= 0)
        {
            die();
        }

        if ((IsHost || IsServer) && isAlive)
        {
            clearCallbackQueue();
            checkDelayedActions();
            time_since_last_tick += Time.deltaTime;
            if (time_since_last_tick > time_between_ticks)
            {
                behaviourTree.Reset();
                behaviourTree.Tick();
                time_since_last_tick = 0f;
            }
        }
    }

    [Task]
    bool enemyClose()
    {
        Vector2 boss_position = transform.position;
        Vector2 player_position;
        foreach(GameObject player in players)
        {
            player_position = player.transform.position;
            if (player.GetComponent<CharacterController>().isAlive && stompAttackRange >= Vector2.Distance(boss_position, player_position)) // TODO: Optimization using distance squared
            {
                return true;
            }
        }
        return false;
    }

    [Task]
    bool offCooldown(string p0)
    {
        switch (p0)
        {
            case "Stomp":
                if (Time.time - last_stomp > StompCooldown) { return true; }
                break;
            case "Landslide":
                if (Time.time - last_landslide > LandslideCooldown) { return true; }
                break;
        }
        return false;
    }

    [Task]
    bool stomp()
    { 
        if (!animationController.finishedAttackAnimation())
        {
            return false;
        }
        last_stomp = Time.time;
        time_of_last_attack = Time.time;
        animationController.toggleStomp();
        addDelayedAction(Time.time + 0.5f, () => { 
            animationController.toggleStomp(); 
        });

        addDelayedAction(Time.time + stompLandingTiming, () => {
            animationController.activateDustEffect();
        });

        return true;
    }

    [Task]
    bool enemiesLinedUp()
    {

        /*
        bool collisionDetection(GameObject player)
        {
            Vector2 player_position = player.transform.position;
            // TODO: Add collision detection for players in the landslide area.
            return false;
        }
        */
        // TODO: Add competent collision detection. Make a strategy for target detection.

        return false;
    }

    [Task]
    bool landslide()
    {
        last_landslide = Time.time;
        animationController.landslideAttack = true;
        addToCallbackQueue(() => { animationController.landslideAttack = false; });
        return true;
    }

    [Task]
    bool stoneThrow()
    {
        if (Time.time - time_of_last_attack < 2.75f) { return false; }
        animationController.toggleStoneThrow();
        float currentTime = Time.time;
        time_of_last_attack = currentTime;
        addToCallbackQueue(() => { animationController.toggleStoneThrow(); });
        animationController.setNewIntensityTarget(2f, currentTime + stoneConjuringStart);

        addDelayedAction(currentTime + stoneConjuringStart, () =>
        {
            animationController.conjureRock();
        });

        addDelayedAction(currentTime + stoneConjuringStart + stoneConjuringDuration, () =>
        {
            // Acquire target player and start tracking.
            Vector2 boss_pos = transform.position;
            if (players.Length == 0) { players = GameObject.FindGameObjectsWithTag("Player"); }
            GameObject closest = players[0];
            float closest_dist = Vector2.Distance(boss_pos, new Vector2(closest.transform.position.x, closest.transform.position.y));
            float dist;
            foreach (GameObject player in players)
            {
                dist = Vector2.Distance(boss_pos, new Vector2(player.transform.position.x, player.transform.position.y));
                if (player.GetComponent<CharacterController>().isAlive && dist < closest_dist)
                {
                    closest_dist = dist;
                    closest = player;
                }
            }
            target_object = closest;
            animationController.setTrackingTarget(target_object.GetComponent<NetworkObject>().OwnerClientId);
            animationController.setNewIntensityTarget(1f, Time.time + 0.5f);
        });

        addDelayedAction(currentTime + stoneThrowTiming, () =>
        {
            // Throw rock at stomach position of target player.
            animationController.throwStone(new Vector2(target_object.transform.position.x, target_object.transform.position.y));
        });

        return true;
    }
}
