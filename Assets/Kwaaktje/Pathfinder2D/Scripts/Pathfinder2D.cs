using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
//using UnityEngine.Profiling;

namespace KwaaktjePathfinder2D
{
    public enum Pathfinder2DResultStatus
    {
        SUCCESS,
        NOT_FOUND
    }

    public struct Pathfinder2DResult
    {
        public List<Vector2Int> Path;
        public Pathfinder2DResultStatus Status;
        public float weightedDistance;
        public float distance;
    }

    public enum NodeConnectionType
    {
        RectangleWithDiagonals,
        RectangleNoDiagonals,
        Hexagone
    }

    /// <summary>
    /// Represents a node in the pathfinding system, containing position, cost values, and parent references.
    /// </summary>
    public class Node : IHeapItem<Node>
    {
        public Vector2Int Vector;
        public float FactDistanceFromStart;
        public float EmpiricalDistanceToEnd;
        public Node Parent;
        private int _index;


        public Node(Vector2Int vector, float factDistanceFromStart, float empiricalDistanceToEnd)
        {
            Vector = vector;
            FactDistanceFromStart = factDistanceFromStart;
            EmpiricalDistanceToEnd = empiricalDistanceToEnd;
        }

        public float GetTotalDistance() { return FactDistanceFromStart + EmpiricalDistanceToEnd; }

        public int CompareTo(Node other)
        {
            return -(GetTotalDistance().CompareTo(other.GetTotalDistance()));
        }
        public int Index
        {
            get { return _index; }
            set { _index = value; }
        }
    }


    /// <summary>
    /// A pathfinding system for a 2D grid-based environment using a weighted graph approach.
    /// </summary>
    public class Pathfinder2D
    {
        protected Dictionary<Vector2Int, float> _weightedMap;
        protected List<Vector2Int> _movementBlockers;
        protected NodeConnectionType _nodeConnectionType = NodeConnectionType.RectangleWithDiagonals;

        /// <summary>
        /// Initializes a new instance of the Pathfinder2D class with a weighted map, movement blockers, and a connection type.
        /// </summary>
        /// <param name="weightedMap">Dictionary mapping grid positions to movement costs.</param>
        /// <param name="movementBlockers">List of grid positions that block movement.</param>
        /// <param name="nodeConnectionType">Defines how nodes are connected (e.g., with or without diagonals).</param>
        public Pathfinder2D(Dictionary<Vector2Int, float> weightedMap, List<Vector2Int> movementBlockers, NodeConnectionType nodeConnectionType)
        {
            _weightedMap = weightedMap;
            _movementBlockers = movementBlockers;
            _nodeConnectionType = nodeConnectionType;
        }

        /// <summary>
        /// Initializes a new instance of the Pathfinder2D class with a weighted map and connection type, without movement blockers.
        /// </summary>
        /// <param name="weightedMap">Dictionary mapping grid positions to movement costs.</param>
        /// <param name="nodeConnectionType">Defines how nodes are connected (e.g., with or without diagonals).</param>
        public Pathfinder2D(Dictionary<Vector2Int, float> weightedMap, NodeConnectionType nodeConnectionType)
        {
            _weightedMap = weightedMap;
            _movementBlockers = new();
            _nodeConnectionType = nodeConnectionType;
        }

        /// <summary>
        /// Finds the shortest path from the start position to the end position using a weighted pathfinding algorithm.
        /// </summary>
        /// <param name="start">The starting grid position.</param>
        /// <param name="end">The target grid position.</param>
        /// <returns>
        /// A list of grid positions representing the path from start to end. Returns an empty list if no valid path exists.
        /// </returns>
        public Pathfinder2DResult FindPath(Vector2Int start, Vector2Int end)
        {
            Pathfinder2DResult result = new Pathfinder2DResult();
            result.Path = new();
            if (start == end)
            {
                result.Status = Pathfinder2DResultStatus.SUCCESS;
                result.weightedDistance = 0;
                result.distance = 0;
                return result;
            }
            if (!(IsEligibleNode(start) & IsEligibleNode(end)))
            {
                result.Status = Pathfinder2DResultStatus.NOT_FOUND;
                result.weightedDistance = 0;
                result.distance = 0;
                return result;
            }

            Heap<Node> opened = new Heap<Node>(_weightedMap.Count);
            HashSet<Node> closed = new HashSet<Node>();
            Dictionary<Vector2Int, Node> vectorToNode = new Dictionary<Vector2Int, Node>();
            Node endNode = new Node(end, float.MaxValue, 0.0f);
            Node startNode = new Node(start, 0.0f, GetEmpiricalDistanceToEndPoint(start, end));
            vectorToNode[start] = startNode;
            vectorToNode[end] = endNode;
            opened.Add(startNode);

            while (opened.Count > 0)
            {
                Node currentNode = opened.Pop();
                closed.Add(currentNode);

                if (currentNode == endNode)
                {
                    break;
                }

                foreach (Vector2Int connectedVector in GetConnectedVectors(currentNode.Vector))
                {
                    if (!vectorToNode.ContainsKey(connectedVector))
                    {
                        Node newNode = new Node(connectedVector, float.MaxValue, GetEmpiricalDistanceToEndPoint(connectedVector, end));
                        vectorToNode.Add(connectedVector, newNode);
                    }

                    Node connectedNode = vectorToNode[connectedVector];
                    if (!closed.Contains(connectedNode))
                    {
                        if (!opened.Contains(connectedNode))
                        {
                            opened.Add(connectedNode);
                        }
                        float newDistance = currentNode.FactDistanceFromStart + _weightedMap[connectedVector];
                        if (newDistance < connectedNode.FactDistanceFromStart)
                        {
                            connectedNode.FactDistanceFromStart = newDistance;
                            connectedNode.Parent = currentNode;
                            opened.Update(connectedNode);
                        }
                    }
                }
            }

            Node currentPathNode = endNode;
            result.Status = Pathfinder2DResultStatus.SUCCESS;
            result.weightedDistance = endNode.GetTotalDistance();
            result.distance = 0;
            while (currentPathNode.Vector != start)
            {
                result.Path.Add(currentPathNode.Vector);
                result.distance += 1;
                if (currentPathNode.Parent is not null)
                {
                    currentPathNode = currentPathNode.Parent;
                }
                else
                {
                    result.Status = Pathfinder2DResultStatus.NOT_FOUND;
                    break;
                }
            }
            if (result.Status == Pathfinder2DResultStatus.NOT_FOUND)
            {
                result.Path = new();
            }
            return result;

        }

        private float GetEmpiricalDistanceToEndPoint(Vector2Int node, Vector2Int end)
        {
            return Mathf.Pow(Mathf.Pow(node.x - end.x, 2) + Mathf.Pow(node.y - end.y, 2), 0.5f);
        }

        private bool IsEligibleNode(Vector2Int node)
        {
            return (_weightedMap.ContainsKey(node)) & (!_movementBlockers.Contains(node));
        }

        private List<Vector2Int> GetConnectedVectors(Vector2Int vector)
        {
            List<Vector2Int> connectedNodes = new();
            if (_nodeConnectionType == NodeConnectionType.RectangleNoDiagonals)
            {
                connectedNodes.Add(new Vector2Int(vector.x + 1, vector.y));
                connectedNodes.Add(new Vector2Int(vector.x - 1, vector.y));
                connectedNodes.Add(new Vector2Int(vector.x, vector.y + 1));
                connectedNodes.Add(new Vector2Int(vector.x, vector.y - 1));
            }
            else if (_nodeConnectionType == NodeConnectionType.RectangleWithDiagonals)
            {
                connectedNodes.Add(new Vector2Int(vector.x + 1, vector.y));
                connectedNodes.Add(new Vector2Int(vector.x - 1, vector.y));
                connectedNodes.Add(new Vector2Int(vector.x, vector.y + 1));
                connectedNodes.Add(new Vector2Int(vector.x, vector.y - 1));
                connectedNodes.Add(new Vector2Int(vector.x + 1, vector.y + 1));
                connectedNodes.Add(new Vector2Int(vector.x - 1, vector.y - 1));
                connectedNodes.Add(new Vector2Int(vector.x - 1, vector.y + 1));
                connectedNodes.Add(new Vector2Int(vector.x + 1, vector.y - 1));
            }
            else if (_nodeConnectionType == NodeConnectionType.Hexagone)
            {
                connectedNodes.Add(new Vector2Int(vector.x + 1, vector.y));
                connectedNodes.Add(new Vector2Int(vector.x - 1, vector.y));
                connectedNodes.Add(new Vector2Int(vector.x, vector.y + 1));
                connectedNodes.Add(new Vector2Int(vector.x, vector.y - 1));
                Math.DivRem(vector.y, 2, out int divRem);
                if (divRem == 0)
                {
                    connectedNodes.Add(new Vector2Int(vector.x - 1, vector.y + 1));
                    connectedNodes.Add(new Vector2Int(vector.x - 1, vector.y - 1));
                }
                else
                {
                    connectedNodes.Add(new Vector2Int(vector.x + 1, vector.y - 1));
                    connectedNodes.Add(new Vector2Int(vector.x + 1, vector.y + 1));
                }
            }

            List<Vector2Int> result = new();
            foreach (Vector2Int vec in connectedNodes)
            {
                if (IsEligibleNode(vec))
                {
                    result.Add(vec);
                }
            }
            return result;
        }

    }
}
