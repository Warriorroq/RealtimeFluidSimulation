using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Project.Helpers
{
    /// <summary>
    /// Utility class for generating simple procedural meshes such as quads and geodesic spheres.
    /// </summary>
    public static class MeshBuilder
    {
        #region Quad Mesh

        /// <summary>
        /// Generates a 1×1 quad centred at the origin on the XY-plane.
        /// </summary>
        public static Mesh GenerateQuadMesh()
        {
            // Clockwise winding order when facing the +Z axis (Unity default).
            int[] indices = { 0, 1, 2, 2, 1, 3 };

            Vector3[] vertices =
            {
                new(-0.5f,  0.5f, 0f),
                new( 0.5f,  0.5f, 0f),
                new(-0.5f, -0.5f, 0f),
                new( 0.5f, -0.5f, 0f)
            };

            Vector2[] uvs =
            {
                new(0f, 1f),
                new(1f, 1f),
                new(0f, 0f),
                new(1f, 0f)
            };

            Mesh mesh = new()
            {
                vertices = vertices,
                uv = uvs
            };
            mesh.SetTriangles(indices, submesh: 0, calculateBounds: true);
            return mesh;
        }

        #endregion

        #region Sphere Mesh

        // Topology description of a unit octahedron. The octahedron is the base
        // polyhedron for the geodesic sphere subdivision.
        private static readonly int[] VertexPairs =
        {
            // Twelve edges represented by their start/end vertex indices.
            0,1, 0,2, 0,3, 0,4,
            1,2, 2,3, 3,4, 4,1,
            5,1, 5,2, 5,3, 5,4
        };

        private static readonly int[] EdgeTriplets =
        {
            // Eight triangular faces described by their three edge indices.
            0,1,4, 1,2,5, 2,3,6, 3,0,7,
            8,9,4, 9,10,5, 10,11,6, 11,8,7
        };

        private static readonly Vector3[] BaseVertices =
        {
            Vector3.up,
            Vector3.left,
            Vector3.back,
            Vector3.right,
            Vector3.forward,
            Vector3.down
        };

        /// <summary>
        /// Generates a geodesic sphere mesh. <paramref name="resolution"/> is
        /// the subdivision count per edge (≥ 0).
        /// </summary>
        public static Mesh GenerateSphereMesh(int resolution)
        {
            int divisions = Mathf.Max(0, resolution);

            int vertsPerFace = ((divisions + 3) * (divisions + 3) - (divisions + 3)) / 2;
            int totalVerts  = vertsPerFace * 8 - (divisions + 2) * 12 + 6;
            int trisPerFace = (divisions + 1) * (divisions + 1);

            var vertices  = new FixedSizeList<Vector3>(totalVerts);
            var triangles = new FixedSizeList<int>(trisPerFace * 8 * 3);

            // Seed octahedron corner vertices.
            vertices.AddRange(BaseVertices);

            // Build all 12 edges with interpolated points.
            Edge[] edges = BuildEdges(divisions, vertices);

            // Construct each triangular face.
            for (int i = 0; i < EdgeTriplets.Length; i += 3)
            {
                bool reverse = (i / 3) >= 4; // Bottom hemisphere uses flipped winding.
                CreateFace(
                    edges[EdgeTriplets[i]],
                    edges[EdgeTriplets[i + 1]],
                    edges[EdgeTriplets[i + 2]],
                    divisions,
                    reverse,
                    vertices,
                    triangles);
            }

            // Assemble the mesh.
            Mesh mesh = new() { vertices = vertices.items };
            mesh.SetTriangles(triangles.items, 0, true);
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Builds interpolated edge data for the geodesic sphere.
        /// </summary>
        private static Edge[] BuildEdges(int divisions, FixedSizeList<Vector3> verts)
        {
            Edge[] edges = new Edge[12];

            for (int i = 0; i < VertexPairs.Length; i += 2)
            {
                Vector3 a = verts.items[VertexPairs[i]];
                Vector3 b = verts.items[VertexPairs[i + 1]];

                int[] indices = new int[divisions + 2];
                indices[0] = VertexPairs[i];

                for (int d = 0; d < divisions; d++)
                {
                    float t = (d + 1f) / (divisions + 1f);
                    indices[d + 1] = verts.nextIndex;
                    verts.Add(Vector3.Slerp(a, b, t));
                }

                indices[^1] = VertexPairs[i + 1];
                edges[i / 2] = new Edge(indices);
            }

            return edges;
        }

        /// <summary>
        /// Creates a single spherical triangle face from three edges.
        /// </summary>
        private static void CreateFace(
            Edge sideA,
            Edge sideB,
            Edge bottom,
            int divisions,
            bool reverseWinding,
            FixedSizeList<Vector3> verts,
            FixedSizeList<int> tris)
        {
            int pointsPerEdge = sideA.vertexIndices.Length;
            int vertsPerFace  = ((divisions + 3) * (divisions + 3) - (divisions + 3)) / 2;
            var map = new FixedSizeList<int>(vertsPerFace);

            map.Add(sideA.vertexIndices[0]); // Apex of the spherical triangle.

            // Inner vertices: walk down from apex to bottom edge.
            for (int row = 1; row < pointsPerEdge - 1; row++)
            {
                // Side A vertex for this row.
                map.Add(sideA.vertexIndices[row]);

                Vector3 a = verts.items[sideA.vertexIndices[row]];
                Vector3 b = verts.items[sideB.vertexIndices[row]];
                int innerCount = row - 1;

                // Interpolate vertices between side A & B.
                for (int j = 0; j < innerCount; j++)
                {
                    float t = (j + 1f) / (innerCount + 1f);
                    map.Add(verts.nextIndex);
                    verts.Add(Vector3.Slerp(a, b, t));
                }

                // Side B vertex.
                map.Add(sideB.vertexIndices[row]);
            }

            // Bottom edge vertices.
            for (int i = 0; i < pointsPerEdge; i++)
                map.Add(bottom.vertexIndices[i]);

            // Triangulate face row by row.
            int rows = divisions + 1;
            for (int row = 0; row < rows; row++)
            {
                int top = ((row + 1) * (row + 1) - row - 1) / 2;
                int bot = ((row + 2) * (row + 2) - row - 2) / 2;
                int trisInRow = 1 + 2 * row;

                for (int col = 0; col < trisInRow; col++)
                {
                    int v0, v1, v2;

                    if ((col & 1) == 0)
                    {
                        v0 = top;
                        v1 = bot + 1;
                        v2 = bot;
                        ++top;
                        ++bot;
                    }
                    else
                    {
                        v0 = top;
                        v1 = bot;
                        v2 = top - 1;
                    }

                    tris.Add(map.items[v0]);
                    tris.Add(map.items[reverseWinding ? v2 : v1]);
                    tris.Add(map.items[reverseWinding ? v1 : v2]);
                }
            }
        }

        #endregion

        #region Helper Types

        /// <summary>
        /// Simple, allocation-free list backed by a fixed-size array.
        /// </summary>
        sealed class FixedSizeList<T>
        {
            public readonly T[] items;
            public int nextIndex;

            public FixedSizeList(int size) => items = new T[size];

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(T item) => items[nextIndex++] = item;

            public void AddRange(IEnumerable<T> source)
            {
                foreach (var s in source) Add(s);
            }
        }

        /// <summary>
        /// Edge wrapper holding precomputed vertex indices for quick access.
        /// </summary>
        sealed class Edge
        {
            public readonly int[] vertexIndices;
            public Edge(int[] indices) => vertexIndices = indices;
        }

        #endregion
    }
} 