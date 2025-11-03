using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeanStudio
{
    public class RuntimeMesh : MonoBehaviour
    {
        public List<Vector3> vertexCoordinates;
        List<MeshTriangle> VisualizationTriangles = new List<MeshTriangle>();

        void Start()
        {
            CreateRuntimeMesh();
        }

        void CreateRuntimeMesh()
        {
            VisualizationTriangles.Clear();
            TriangulateSide1();
            TriangulateSide2();
            CreateAreaMesh();
        }

        /// <summary>
        /// If the index is larger than the length, it will be loop around.
        /// </summary>
        /// <param name="index"> index to transform </param>
        /// <param name="length"> maximum value -> length of the array </param>
        /// <returns></returns>
        int CircularIndex(int index, int length)
        {
            int temp = index % length;
            int temp1 = temp + length;
            if (temp < 0)
                return temp1;

            return temp;
        }

        /// <summary>
        /// Checks if this point id convex.
        /// </summary>
        /// <param name="prevpoint"> first point </param>
        /// <param name="currpoint"> second point </param>
        /// <param name="nxtpoint"> third point </param>
        /// <returns></returns>
        bool PointIsConvex(Vector3 prevpoint, Vector3 currpoint, Vector3 nxtpoint)
        {
            Vector3 temp1 = prevpoint - currpoint;
            Vector3 temp2 = nxtpoint - currpoint;
            temp1.Normalize();
            temp2.Normalize();
            Vector3 temp3 = Vector3.Cross(temp1, temp2);
            return temp3.y < 0;
        }

        /// <summary>
        /// Checks if this is a valid point.
        /// </summary>
        /// <param name="t1"> first point </param>
        /// <param name="t2"> second point </param>
        /// <param name="t3"> third point </param>
        /// <param name="p"></param>
        /// <returns></returns>
        bool IsPointInTriangle(Vector3 t1, Vector3 t2, Vector3 t3, Vector3 p)
        {
            Vector3 v1 = t3 - t1;
            Vector3 v2 = t2 - t1;
            Vector3 v3 = p - t1;

            float daa = Vector3.Dot(v1, v1);
            float dab = Vector3.Dot(v1, v2);
            float dac = Vector3.Dot(v1, v3);
            float dbb = Vector3.Dot(v2, v2);
            float dbc = Vector3.Dot(v2, v3);

            float u = (((dbb * dac) - (dab * dbc)) / ((daa * dbb) - (dab * dab)));
            float v = (((daa * dbc) - (dab * dac)) / ((daa * dbb) - (dab * dab)));

            return (v >= 0.0f) && (u >= 0.0f) && (u + v < 1.0f);
        }

        /// <summary>
        /// Checks if any reflex points are inside the triangle created by the 3 points. If so, it is an "Ear".
        /// </summary>
        /// <param name="currPointIndex"> Point index that you want to check </param>
        /// <param name="reflexIndices"> Array that contains all the known reflex indices </param>
        /// <param name="vCoordinates"> Array that contains all the vertex coordinates positional data </param>
        /// <returns></returns>
        bool IsPointAnEar(int currPointIndex, ref List<int> reflexIndices, ref List<Vector3> vCoordinates)
        {
            bool IsEar = true;
            int prevIndex = CircularIndex(currPointIndex - 1, vCoordinates.Count);
            int nextIndex = CircularIndex(currPointIndex + 1, vCoordinates.Count);

            Vector3 currPoint = vCoordinates[currPointIndex];
            Vector3 prevPoint = vCoordinates[prevIndex];
            Vector3 nextPoint = vCoordinates[nextIndex];

            for (int i = 0; i < reflexIndices.Count; i++)
            {
                IsEar = !IsPointInTriangle(currPoint, prevPoint, nextPoint, vCoordinates[reflexIndices[i]]);
                if (!IsEar)
                    return IsEar;
            }

            return IsEar;
        }

        /// <summary>
        /// Trianglate the vertex coordinates to render one side of the triangle.
        /// </summary>
        void TriangulateSide1()
        {
            List<Vector3> vCoordinates = new List<Vector3>(vertexCoordinates);
            List<Vector3> reflexPoints = new List<Vector3>();
            List<Vector3> convexPoints = new List<Vector3>();
            List<Vector3> earPoints = new List<Vector3>();

            List<int> reflexIndices = new List<int>();
            List<int> convexIndices = new List<int>();
            List<int> earIndices = new List<int>();

            GetPolygonComponents(ref vCoordinates, ref reflexPoints, ref convexPoints, ref earPoints, ref reflexIndices, ref convexIndices, ref earIndices);

            reflexPoints.Clear();
            convexPoints.Clear();
            earPoints.Clear();

            TrianglesFromPointsSide1(ref vCoordinates, ref convexIndices, ref reflexIndices, ref earIndices);
        }

        void TrianglesFromPointsSide1(ref List<Vector3> vCoordinates, ref List<int> inConvexIndices, ref List<int> inReflexIndices, ref List<int> inEarIndices)
        {
            int currPoint = 0;
            if (inEarIndices.Count > 0)
                currPoint = inEarIndices[0];
            int prevPoint = CircularIndex(currPoint - 1, vCoordinates.Count);
            int nextPoint = CircularIndex(currPoint + 1, vCoordinates.Count);

            if (vCoordinates.Count <= 3)
            {
                MeshTriangle triangle;
                triangle.point1 = vCoordinates[0];
                triangle.point2 = vCoordinates[1];
                triangle.point3 = vCoordinates[2];
                VisualizationTriangles.Add(triangle);
            }
            else
            {
                // Make a triangle of the ear point and its adjacent points.
                MeshTriangle triangle;
                triangle.point1 = vCoordinates[currPoint];
                triangle.point2 = vCoordinates[prevPoint];
                triangle.point3 = vCoordinates[nextPoint];

                // Remove the ear.
                vCoordinates.RemoveAt(currPoint);

                // Recalculate the polyogon components with the ear missing for an optimisation a linked list data structure can be used instead of recalculating at every step.
                List<Vector3> reflexPoints = new List<Vector3>();
                List<Vector3> convexPoints = new List<Vector3>();
                List<Vector3> earPoints = new List<Vector3>();
                List<int> reflexIndices = new List<int>();
                List<int> convexIndices = new List<int>();
                List<int> earIndices = new List<int>();

                GetPolygonComponents(ref vCoordinates, ref reflexPoints, ref convexPoints, ref earPoints, ref reflexIndices, ref convexIndices, ref earIndices);
                TrianglesFromPointsSide1(ref vCoordinates, ref convexIndices, ref reflexIndices, ref earIndices);
                VisualizationTriangles.Add(triangle);
            }
        }

        /// <summary>
        /// Create thye other side of the triangle essentially creating a double sided mesh.
        /// </summary>
        void TriangulateSide2()
        {
            List<Vector3> vCoordinates = new List<Vector3>(vertexCoordinates);
            List<Vector3> reflexPoints = new List<Vector3>();
            List<Vector3> convexPoints = new List<Vector3>();
            List<Vector3> earPoints = new List<Vector3>();

            List<int> reflexIndices = new List<int>();
            List<int> convexIndices = new List<int>();
            List<int> earIndices = new List<int>();

            GetPolygonComponents(ref vCoordinates, ref reflexPoints, ref convexPoints, ref earPoints, ref reflexIndices, ref convexIndices, ref earIndices);

            reflexPoints.Clear();
            convexPoints.Clear();
            earPoints.Clear();

            TrianglesFromPointsSide2(ref vCoordinates, ref convexIndices, ref reflexIndices, ref earIndices);
        }


        void TrianglesFromPointsSide2(ref List<Vector3> vCoordinates, ref List<int> inConvexIndices, ref List<int> inReflexIndices, ref List<int> inEarIndices)
        {
            int currPoint = 0;
            if (inEarIndices.Count > 0)
                currPoint = inEarIndices[0];
            int prevPoint = CircularIndex(currPoint - 1, vCoordinates.Count);
            int nextPoint = CircularIndex(currPoint + 1, vCoordinates.Count);

            if (vCoordinates.Count <= 3)
            {
                MeshTriangle triangle;
                triangle.point1 = vCoordinates[2];
                triangle.point2 = vCoordinates[1];
                triangle.point3 = vCoordinates[0];
                VisualizationTriangles.Add(triangle);
            }
            else
            {
                // Make a triangle of the ear point and its adjacent points.
                MeshTriangle triangle;
                triangle.point1 = vCoordinates[nextPoint];
                triangle.point2 = vCoordinates[prevPoint];
                triangle.point3 = vCoordinates[currPoint];

                // Remove the ear.
                vCoordinates.RemoveAt(currPoint);

                // Recalculate the polyogon components with the ear missing for an optimisation a linked list data structure can be used instead of recalculating at every step.
                List<Vector3> reflexPoints = new List<Vector3>();
                List<Vector3> convexPoints = new List<Vector3>();
                List<Vector3> earPoints = new List<Vector3>();
                List<int> reflexIndices = new List<int>();
                List<int> convexIndices = new List<int>();
                List<int> earIndices = new List<int>();

                GetPolygonComponents(ref vCoordinates, ref reflexPoints, ref convexPoints, ref earPoints, ref reflexIndices, ref convexIndices, ref earIndices);
                TrianglesFromPointsSide2(ref vCoordinates, ref convexIndices, ref reflexIndices, ref earIndices);
                VisualizationTriangles.Add(triangle);
            }
        }

        void TrianglesToIndices(ref List<MeshTriangle> triangles, ref List<Vector3> vertices, ref List<int> indices)
        {
            for (int i = 0; i < triangles.Count; i++)
            {
                int newIndex = 0;
                if (!vertices.Contains(triangles[i].point1))
                {
                    vertices.Add(triangles[i].point1);
                    newIndex = vertices.IndexOf(triangles[i].point1);
                }

                if (newIndex < 0)
                    indices.Add(newIndex);
                else
                    indices.Add(vertices.IndexOf(triangles[i].point1));

                if (!vertices.Contains(triangles[i].point2))
                {
                    vertices.Add(triangles[i].point2);
                    newIndex = vertices.IndexOf(triangles[i].point2);
                }

                if (newIndex < 0)
                    indices.Add(newIndex);
                else
                    indices.Add(vertices.IndexOf(triangles[i].point2));

                if (!vertices.Contains(triangles[i].point3))
                {
                    vertices.Add(triangles[i].point3);
                    newIndex = vertices.IndexOf(triangles[i].point3);
                }

                if (newIndex < 0)
                    indices.Add(newIndex);
                else
                    indices.Add(vertices.IndexOf(triangles[i].point3));
            }
        }

        void GetPolygonComponents(ref List<Vector3> vCoordinates, ref List<Vector3> reflexPoints, ref List<Vector3> convexPoints, ref List<Vector3> earPoints, ref List<int> reflexIndices, ref List<int> convexIndices, ref List<int> earIndices)
        {
            Vector3 prevPoint;
            Vector3 nextPoint;

            {
                for (int currIndex = 0; currIndex < vCoordinates.Count; currIndex++)
                {
                    Vector3 currPoint = vCoordinates[currIndex];
                    int prevIndex = CircularIndex(currIndex - 1, vCoordinates.Count);
                    prevPoint = vCoordinates[prevIndex];
                    int nextIndex = CircularIndex(currIndex + 1, vCoordinates.Count);
                    nextPoint = vCoordinates[nextIndex];

                    if (PointIsConvex(prevPoint, currPoint, nextPoint))
                    {
                        convexPoints.Add(currPoint);
                        convexIndices.Add(currIndex);
                    }
                    else
                    {
                        reflexPoints.Add(currPoint);
                        reflexIndices.Add(currIndex);
                    }
                }
            }

            {
                for (int currIndex = 0; currIndex < convexIndices.Count; currIndex++)
                {
                    int currElement = convexIndices[currIndex];

                    if (IsPointAnEar(currElement, ref reflexIndices, ref vCoordinates))
                    {
                        earPoints.Add(vCoordinates[currElement]);
                        earIndices.Add(currElement);
                    }
                }
            }
        }

        void CreateAreaMesh()
        {
            // Finally creating the actual mesh with the triangles have been calculated.

            List<Vector3> vertices = new List<Vector3>();
            List<int> indices = new List<int>();
            TrianglesToIndices(ref VisualizationTriangles, ref vertices, ref indices);
            List<Vector3> normals = new List<Vector3>();

            for (int i = 0; i < vertices.Count; i++)
            {
                normals.Add(new Vector3(0, 1, 0));
            }
            List<Vector2> uv = new List<Vector2>();
            for (int i = 0; i < vertices.Count; i++)
            {
                uv.Add(new Vector2(0, 0));
            }
            List<Color> vertexColors = new List<Color>();
            for (int i = 0; i < vertices.Count; i++)
            {
                vertexColors.Add(new Color(0.75f, 0.75f, 0.75f, 1.0f));
            }

            Mesh createdMesh = new Mesh();
            createdMesh.vertices = vertices.ToArray();
            createdMesh.triangles = indices.ToArray();
            createdMesh.normals = normals.ToArray();
            createdMesh.uv = uv.ToArray();
            createdMesh.colors = vertexColors.ToArray();

            gameObject.GetComponent<MeshFilter>().mesh = createdMesh;
            gameObject.GetComponent<MeshRenderer>().material = new Material(Resources.Load("M_DefaultMaterial", typeof(Material)) as Material);
        }

        // Struct representing a triangle.
        struct MeshTriangle
        {
            public Vector3 point1;
            public Vector3 point2;
            public Vector3 point3;
        }
    }
}