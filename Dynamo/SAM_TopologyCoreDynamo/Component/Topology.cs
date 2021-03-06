﻿using System.Collections.Generic;

namespace SAM_TopologicCoreDynamo
{
    public static class Topology
    {
        public static string Analyze(object topology)
        {
            return ((Topologic.Topology)topology).Analyze();
        }

        public static IEnumerable<object> ToCellComplex(object topology)
        {
            return ((Topologic.Topology)topology).CellComplexes;
        }

        public static IEnumerable<object> ToCells(object topology)
        {
            return ((Topologic.Topology)topology).Cells;
        }

        public static IEnumerable<object> ToFaces(object topology)
        {
            return ((Topologic.Topology)topology).Faces;
        }

        public static List<global::Topologic.Wire> ToWires(Topologic.Topology topology)
        {
            return topology.Wires;
        }

        public static List<global::Topologic.Vertex> ToVertices(Topologic.Topology topology)
        {
            return topology.Vertices;
        }
    }
}