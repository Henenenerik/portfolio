using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using UnityEngine.Assertions;
using sys_di = System.Diagnostics;


/// <summary>
/// Written by Henrik Johansson.
/// Sections highlighted in portfolio are searchable with #Portfolio.
/// 
/// This is a script used in project in the game engine Unity. 
/// If you are interested in the rest of the project please let me know.
/// </summary>


public class Map3D : MonoBehaviour
{
    public TerrainManager terrain;

    // #Portfolio
    // A 3D grid to store space-time map positions.
    private MapPosition3D[,,] map;
    public int resolution;
    public int max_time_steps;
    private int unvisited_count;

    public float x_step_size;
    public float z_step_size;
    private Vector3 start_pos;

    private GameObject[] enemies_arr;
    private List<GameObject> enemies;

    private bool init = false;
    private bool adjacent_node = true;

    private PriorityQueue<SearchNode> astar_frontier;

    // #Portfolio
    // Search node includes 3 dimensions
    private struct SearchNode : IEquatable<SearchNode>
    {
        public int x;
        public int y;
        public int x_parent;
        public int y_parent;
        public float cost;
        public int t;

        public bool Equals(SearchNode other)
        {
            return other.x == x && other.y == y;
        }
    };

    public enum HeuristicChoice { Manhattan, Euclidean, Diagonal };
    public HeuristicChoice heuristic_choice;

    private sys_di.Stopwatch stop_watch;
    public float time_scale;

    public void Start()
    {
        map = new MapPosition3D[resolution, resolution, max_time_steps];
        x_step_size = (terrain.myInfo.x_high - terrain.myInfo.x_low) / (float)resolution;
        z_step_size = (terrain.myInfo.z_high - terrain.myInfo.z_low) / (float)resolution;
        start_pos = new Vector3(terrain.myInfo.x_low + x_step_size / 2f, 0.1f, terrain.myInfo.z_low + z_step_size / 2f);

        Vector3 position = start_pos;
        int x, y;
        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                for (int t = 0; t < max_time_steps; t++)
                {
                    (x, y) = ComputeTraversabilityIndicies(position);
                    map[i, j, t] = new MapPosition3D(i, j, t, terrain.myInfo.traversability[x, y] == 1f);

                }
                position.z += z_step_size;
            }
            position.z = terrain.myInfo.z_low + z_step_size / 2f;
            position.x += x_step_size;
        }

        init = true;
        if (time_scale != 0)
        {
            Time.timeScale = time_scale;
        }
    }

    public void StartTimer()
    {
        if (stop_watch == null) { stop_watch = new sys_di.Stopwatch(); }
        stop_watch.Start();
    }

    public void StopTimer()
    {
        stop_watch.Stop();
        TimeSpan ts = stop_watch.Elapsed;
        string elapsedTime = String.Format("{0:00}h:{1:00}m:{2:00}s.{3:000}ms",
            ts.Hours, ts.Minutes, ts.Seconds,
            ts.Milliseconds);
        Console.WriteLine("RunTime " + elapsedTime);
    }

    private (int, int) ComputeTraversabilityIndicies(Vector3 point)
    {
        float x_step_size = (terrain.myInfo.x_high - terrain.myInfo.x_low) / terrain.myInfo.x_N;
        float z_step_size = (terrain.myInfo.x_high - terrain.myInfo.x_low) / terrain.myInfo.x_N;
        int x = (int)((point.x - terrain.myInfo.x_low) / x_step_size);
        int z = (int)((point.z - terrain.myInfo.z_low) / z_step_size);
        return (x, z);
    }

    private (int, int) ComputeMapIndicies(Vector3 point)
    {
        int x = (int)((point.x - terrain.myInfo.x_low) / x_step_size);
        int z = (int)((point.z - terrain.myInfo.z_low) / z_step_size);
        return (x, z);
    }

    private Vector3 IndiciesToVector(int i, int j)
    {
        return IndiciesToVector(i, j, 0.5f);
    }

    private Vector3 IndiciesToVector(int i, int j, float y_offset)
    {
        float x = terrain.myInfo.x_low + ((float)i + 0.5f) * x_step_size;
        float z = terrain.myInfo.z_low + ((float)j + 0.5f) * z_step_size;
        return new Vector3(x, y_offset, z);
    }

    private bool ValidIndicies(int x, int y)
    {
        return x >= 0 && x < resolution && y >= 0 && y < resolution;
    }

    private bool ValidVector(Vector3 v)
    {
        return v.x >= terrain.myInfo.x_low && v.x <= terrain.myInfo.x_high && v.z >= terrain.myInfo.z_low && v.z <= terrain.myInfo.z_high;
    }

    public int DistanceDiagonal(int a_x, int a_y, int b_x, int b_y)
    {
        return Math.Max(Math.Abs(a_x - b_x), Math.Abs(a_y - b_y));
    }

    private bool Adjacent(int a, int b, int x, int y)
    {
        return DistanceDiagonal(a, b, x, y) < 2;
    }

    private bool DiagonalWall(int a, int b, int x, int y, int t)
    {
        if (Adjacent(a, b, x, y) == false) { return false; }
        return (ValidIndicies(x, b) && map[x, b, t].wall == true) || (ValidIndicies(a, y) && map[a, y, t].wall == true);
    }

    private bool DiagonalOccupation(int a, int b, int parent_x, int parent_y, int t)
    {
        return a != parent_x && b != parent_y && ((map[parent_x, b, t - 1].occupied && map[a, parent_y, t].occupied) || (map[parent_x, b, t].occupied && map[a, parent_y, t - 1].occupied));
    }

    public List<Vector3> AStar(Vector3 start, Vector3 target)
    {
        if (astar_frontier == null) { astar_frontier = new PriorityQueue<SearchNode>(resolution * resolution * max_time_steps); }
        astar_frontier.Clear();
        (int x_start, int y_start) = ComputeMapIndicies(start);
        (int x_target, int y_target) = ComputeMapIndicies(target);
        SearchNode root = new SearchNode { x = x_start, y = y_start, x_parent = -1, y_parent = -1, cost = 0f, t = 0 };
        astar_frontier.Insert(0f, root);
        Dictionary<(int, int, int), SearchNode> visited = new Dictionary<(int, int, int), SearchNode>();

        float Manhattan(int i, int j)
        {
            return Math.Abs(i - x_target) + Math.Abs(j - y_target);
        }

        float Euclidean(int i, int j)
        {
            return Mathf.Sqrt((i - x_target) * (i - x_target) + (j - y_target) * (j - y_target));
        }

        float Diagonal(int i, int j)
        {
            return Math.Max(Math.Abs(i - x_target), Math.Abs(j - y_target));
        }

        float Heuristic(int i, int j)
        {
            switch (heuristic_choice)
            {
                case HeuristicChoice.Diagonal:
                    return Diagonal(i, j);
                case HeuristicChoice.Euclidean:
                    return Euclidean(i, j);
                case HeuristicChoice.Manhattan:
                    return Manhattan(i, j);
                default:
                    return 0f;
            }
        }

        void UpdateVisited(SearchNode node_, Action<SearchNode> func)
        {
            SearchNode existed;
            if (visited.TryGetValue((node_.x, node_.y, node_.t), out existed))
            {
                if (existed.cost > node_.cost)
                {

                    existed.x_parent = node_.x_parent;
                    existed.y_parent = node_.y_parent;
                    existed.cost = node_.cost;
                    astar_frontier.UpdateCost(node_.cost, existed);
                }
            }
            else
            {
                visited.Add((node_.x, node_.y, node_.t), node_);
                func(node_);
            }
        }

        // #Portfolio
        // When adding successors in the A* algorithm, check to see if a position is occupied in the grid both current and previous time step.
        // This is to avoid the swapping problem where two adjacent drones collide because the want to swap position.
        void AddSuccessor(int i, int j, int t, SearchNode parent, float cost)
        {
            if (ValidIndicies(i, j) && 
                map[i, j, t].wall == false && 
                DiagonalWall(i, j, parent.x, parent.y, t) == false && 
                map[i, j, t].occupied == false &&
                map[i, j, t - 1].occupied == false &&
                DiagonalOccupation(i, j, parent.x, parent.y, t) == false
                )
            {
                SearchNode successor = new SearchNode { x = i, y = j, x_parent = parent.x, y_parent = parent.y, cost = parent.cost + cost, t=t };
                UpdateVisited(successor, x => astar_frontier.Insert(x.cost, x));
            }
        }

        List<Vector3> GeneratePath(SearchNode node_)
        {
            List<Vector3> res = new List<Vector3>() { IndiciesToVector(node_.x, node_.y) };
            SearchNode debug;
            while (node_.x_parent != -1 && node_.y_parent != -1)
            {
                debug = node_;
                if (visited.TryGetValue((node_.x_parent, node_.y_parent, node_.t - 1), out node_))
                {
                    map[node_.x, node_.y, node_.t].occupied = true;
                    res.Add(IndiciesToVector(node_.x, node_.y));
                }
                else
                {
                    Debug.Log("Whoah dude. Mega error when generating AStar path. Scary vibes.");
                    break;
                }
            }

            (int x, int y) = ComputeMapIndicies(res[0]);
            for (int count = res.Count; count < max_time_steps; count++)
            {
                // #Portfolio
                // Here the map is updated to reflect which positions a drone will occupy during its path.
                // The map is not reset between executions.
                map[x, y, count].occupied = true;
            }

            res.Reverse();
            return res;
        }

        SearchNode node;
        int infinity_break = 100000;
        int k = 0;
        int time_step = 0;

        while (astar_frontier.count > 0)
        {
            Assert.IsTrue(astar_frontier.VerifyQueue(), "broken prio queue");
            node = astar_frontier.ExtractMin();

            if (node.x == x_target && node.y == y_target)
            {
                return GeneratePath(node);
            }
            time_step = node.t + 1;
            if (time_step < max_time_steps)
            {
                // Removed successors on the diagonals.
                //AddSuccessor(node.x + 1, node.y + 1, time_step, node, 1.414f + Heuristic(node.x + 1, node.y + 1));
                AddSuccessor(node.x + 1, node.y    , time_step, node, 1f     + Heuristic(node.x + 1, node.y));
                AddSuccessor(node.x,     node.y + 1, time_step, node, 1f     + Heuristic(node.x,     node.y + 1));
                //AddSuccessor(node.x - 1, node.y - 1, time_step, node, 1.414f + Heuristic(node.x - 1, node.y - 1));
                AddSuccessor(node.x - 1, node.y    , time_step, node, 1f     + Heuristic(node.x - 1, node.y));
                AddSuccessor(node.x    , node.y - 1, time_step, node, 1f     + Heuristic(node.x,     node.y - 1));
                //AddSuccessor(node.x + 1, node.y - 1, time_step, node, 1.414f + Heuristic(node.x + 1, node.y - 1));
                //AddSuccessor(node.x - 1, node.y + 1, time_step, node, 1.414f + Heuristic(node.x - 1, node.y + 1));
                
                // TODO: Update heuristic to better reflect time. Right now, standing still has an arbitrary cost.
                AddSuccessor(node.x, node.y, time_step, node, 1f + Heuristic(node.x, node.y));
            }
            
            UpdateVisited(node, x => { });
        }

        throw new Exception("No path found");
    }

    public List<(int, int)> VectorPathToIndexPath(List<Vector3> path)
    {
        List<(int, int)> new_path = new List<(int, int)>();
        foreach (Vector3 node in path)
        {
            new_path.Add(ComputeMapIndicies(node));
        }
        return new_path;
    }

    public void DebugDrawMap(float timer)
    {
        float x_step_size = (terrain.myInfo.x_high - terrain.myInfo.x_low) / (float)resolution;
        float z_step_size = (terrain.myInfo.z_high - terrain.myInfo.z_low) / (float)resolution;
        Vector3 position = new Vector3(terrain.myInfo.x_low + x_step_size / 2f, 0.1f, terrain.myInfo.z_low + z_step_size / 2f);
        int number_of_enemies = enemies != null ? enemies.Count : 1;
        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                map[i, j, 0].DebugDrawMapPosition(position, x_step_size / 2f, z_step_size / 2f, number_of_enemies, timer);
                position.z += z_step_size;
            }
            position.z = terrain.myInfo.z_low + z_step_size / 2f;
            position.x += x_step_size;
        }
    }

    public void FixedUpdate()
    {
        //DebugDrawMap(Time.fixedDeltaTime);
    }
}


public class MapPosition3D
{
    public int x;
    public int y;
    public int t;

    public bool wall;
    public bool seen;
    public bool occupied = false;

    public MapPosition3D(int x, int y, int t, bool wall)
    {
        this.x = x;
        this.y = y;
        this.t = t;
        this.wall = wall;
        seen = wall;
    }

    public void DebugDrawMapPosition(Vector3 position, float x_len, float z_len, int num_enemies, float timer)
    {
        if (wall) { return; }
        Vector3 top = position;
        top.z += z_len;
        Vector3 bottom = position;
        bottom.z -= z_len;
        Vector3 left = position;
        left.x -= x_len;
        Vector3 right = position;
        right.x += x_len;

        Color c = Color.black;
        //c.b = ((float)num_visible_enemies) / (float)num_enemies;

        Debug.DrawLine(top, bottom, c, timer);
        Debug.DrawLine(left, right, c, timer);
    }

}