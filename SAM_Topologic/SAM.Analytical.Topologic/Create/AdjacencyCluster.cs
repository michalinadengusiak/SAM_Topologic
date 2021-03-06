﻿using SAM.Core;
using SAM.Geometry.Spatial;
using SAM.Geometry.Topologic;
using System;
using System.Collections.Generic;
using System.Linq;
using Topologic;
using Topologic.Utilities;

namespace SAM.Analytical.Topologic
{
    public static partial class Create
    {
        public static AdjacencyCluster AdjacencyCluster(IEnumerable<Space> spaces, IEnumerable<Panel> panels, out List<Topology> topologies, double minArea = Tolerance.MacroDistance, bool updatePanels = true, bool tryCellComplexByCells = true, Log log = null, double silverSpacing = Tolerance.MacroDistance, double tolerance = Tolerance.Distance)
        {
            Log.Add(log, "Method Name: {0}, Tolerance: {1}, Update Panels: {2}", "SAM.Analytical.Topologic.Create.AdjacencyCluster", tolerance, updatePanels);

            topologies = null;

            AdjacencyCluster result = new AdjacencyCluster();
            result.AddObjects(spaces);
            result.AddObjects(panels);

            List<Face> faceList = new List<Face>();

            int index = 1;
            foreach (Panel panel in result.GetObjects<Panel>())
            {
                if (panel == null)
                    continue;

                Face face = Convert.ToTopologic(panel);
                if (face == null)
                    continue;

                faceList.Add(face);
                Log.Add(log, "Face {0:D4} added. Panel [{1}]", index, panel.Guid);
                index++;
            }

            if (faceList == null || faceList.Count == 0)
                return null;

            topologies = new List<Topology>();
            List<Cell> cells = null;
            if (tryCellComplexByCells)
            {
                try
                {
                    Log.Add(log, "Trying to make CellComplex By Cells");
                    Cluster cluster = Cluster.ByTopologies(faceList);
                    Log.Add(log, "Cluster.ByTopologies Done");
                    Topology topology = cluster.SelfMerge();
                    Log.Add(log, "Cluster SelfMerge Done");
                    if (topology.Cells != null && topology.Cells.Count != 0)
                    {
                        cells = topology.Cells;
                        CellComplex cellComplex = null;
                        try
                        {
                            cellComplex = CellComplex.ByCells(cells);
                        }
                        catch (Exception exception)
                        {
                            Log.Add(log, "Cells could not be taken from CellComplex");
                            Log.Add(log, "Exception Message: {0}", exception.Message);
                        }
                        
                        if (cellComplex != null && cellComplex.Cells != null && cellComplex.Cells.Count != 0)
                        {
                            topologies.Add(cellComplex);
                            
                            cells = cellComplex.Cells;
                        }
                        else
                        {
                            topologies.Add(topology);
                            Log.Add(log, "Cells taken from Cluster");
                        }
                    }

                }
                catch (Exception exception)
                {
                    Log.Add(log, "Cannot create CellComplex By Cells or Cells form Cluster SelfMerge");
                    Log.Add(log, "Exception Message: {0}", exception.Message);
                    cells = null;
                }
            }

            if (cells == null)
            {
                try
                {
                    Log.Add(log, "Trying to make CellComplex By Faces");
                    CellComplex cellComplex = CellComplex.ByFaces(faceList, tolerance);
                    topologies.Add(cellComplex);
                    cells = cellComplex.Cells;
                    Log.Add(log, "CellComplex By Faces Created");
                }
                catch (Exception exception)
                {
                    Log.Add(log, "Cannot create CellComplex By Faces");
                    Log.Add(log, "Exception Message: {0}", exception.Message);

                    cells = null;
                }
            }

            if (cells == null || cells.Count == 0)
            {
                Log.Add(log, "No cells found");
                return null;
            }

            List<Space> spaces_Temp = new List<Space>();
            if (spaces != null)
                spaces_Temp.AddRange(spaces);

            List<Geometry.Spatial.Shell> shells = cells.ToSAM();
            Log.Add(log, "Single CellComplex converted to shells");

            if (shells == null)
                return null;

            HashSet<Guid> guids_Updated = new HashSet<Guid>();

            Dictionary<Panel, Face3D> dictionary_Panel_Face3D = new Dictionary<Panel, Face3D>();
            result.GetObjects<Panel>().ForEach(x => dictionary_Panel_Face3D[x] = x.GetFace3D());

            index = 1;
            List<Tuple<Panel, Point3D>> tuples_InternalPoint3D = new List<Tuple<Panel, Point3D>>();
            foreach (Geometry.Spatial.Shell shell in shells)
            {
                if (shell == null)
                    return null;

                Log.Add(log, "Simplifying shell");
                shell.Simplify(tolerance);

                Log.Add(log, "Extracting faces from shell");
                List<Face3D> face3Ds = shell?.Face3Ds;
                if (face3Ds == null)
                {
                    Log.Add(log, "No face2Ds found in Shell");
                    continue;
                }
                    
                List<Space> spaces_Shell = spaces_Temp.FindAll(x => shell.InRange(x.Location, tolerance) || shell.Inside(x.Location, silverSpacing, tolerance));
                if (spaces_Shell.Count != 0)
                {
                    //Handling cases where Space Location is on the floor
                    if(spaces_Shell.Count > 1)
                    {
                        Vector3D vector3D = Vector3D.WorldZ * silverSpacing;
                        List<Point3D> point3D_Locations = spaces_Shell.ConvertAll(x => (Point3D)x.Location.GetMoved(vector3D));
                        List<Point3D> point3D_Locations_Temp = point3D_Locations.FindAll(x => shell.InRange(x, tolerance) || shell.Inside(x, silverSpacing, tolerance));
                        if(point3D_Locations_Temp != null && point3D_Locations_Temp.Count > 0)
                        {
                            List<Space> spaces_Shell_Temp = point3D_Locations_Temp.ConvertAll(x => point3D_Locations.IndexOf(x)).ConvertAll(x => spaces_Shell[x]);
                            if (spaces_Shell_Temp != null && spaces_Shell_Temp.Count != 0)
                                spaces_Shell = spaces_Shell_Temp;
                        }

                    }

                    spaces_Temp.RemoveAll(x => spaces_Shell.Contains(x));
                }
                else
                {
                    Log.Add(log, "Creating new Space");

                    Point3D location = shell.InternalPoint3D(silverSpacing, tolerance);
                    if (location == null)
                        continue;

                    Space space = new Space("Cell " + index, location);
                    index++;

                    if (!result.AddObject(space))
                        continue;

                    spaces_Shell = new List<Space>() { space };
                }

                if (spaces_Shell == null || spaces_Shell.Count == 0)
                    continue;

                Log.Add(log, "Upadting Panels");
                foreach (Face3D face3D in face3Ds)
                {
                    if (minArea != 0 && face3D.GetArea() <= minArea)
                    {
                        Log.Add(log, "Face3D is too small");
                        continue;
                    }

                    Log.Add(log, "Looking for existing Panel");
                    Tuple <Panel, Point3D> tuple_InternalPoint3D = tuples_InternalPoint3D.Find(x => face3D.Inside(x.Item2, tolerance));
                    if(tuple_InternalPoint3D != null)
                    {
                        Log.Add(log, "Existing Panel found: {0}", tuple_InternalPoint3D.Item1.Guid);
                        foreach (Space space in spaces_Shell)
                        {
                            if (result.AddRelation(space, tuple_InternalPoint3D.Item1))
                                Log.Add(log, "Space [{0}] and Panel [{1}] relation added", space.Guid, tuple_InternalPoint3D.Item1.Guid);
                        }
                        continue;
                    }

                    Log.Add(log, "Looking for old Panel");
                    Panel panel_Old = Query.FindPanel(face3D, dictionary_Panel_Face3D);
                    if (panel_Old == null)
                        continue;

                    Log.Add(log, "Old Panel found: {0}", panel_Old.Guid);                  

                    Panel panel_New = null;

                    if (updatePanels)
                    {
                        if (guids_Updated.Contains(panel_Old.Guid))
                        {
                            panel_New = new Panel(Guid.NewGuid(), panel_Old, face3D);
                            Log.Add(log, "Creating new Panel for Old Panel [{0}]. New Panel [{1}]", panel_Old.Guid, panel_New.Guid);
                        }
                        else
                        {
                            panel_New = new Panel(panel_Old.Guid, panel_Old, face3D);
                            guids_Updated.Add(panel_Old.Guid);
                            Log.Add(log, "Updating Panel [{0}] with new geometry", panel_New.Guid);
                        }

                        result.AddObject(panel_New);
                    }
                    else
                    {
                        panel_New = new Panel(panel_Old.Guid, panel_Old, face3D);
                        Log.Add(log, "Creating temporary Panel for Panel [{0}]", panel_New.Guid);
                    }

                    if (panel_New == null)
                        continue;

                    tuples_InternalPoint3D.Add(new Tuple<Panel, Point3D>(panel_New, face3D.InternalPoint3D(tolerance)));

                    foreach (Space space in spaces_Shell)
                    {
                        if (result.AddRelation(space, panel_New))
                            Log.Add(log, "Space [{0}] and Panel [{1}] relation added", space.Guid, panel_New.Guid);
                    }

                    Log.Add(log, "Adding face finished");
                }
            }

            Log.Add(log, "Sucesfully completed");
            return result;
        }

        public static AdjacencyCluster AdjacencyCluster_Depreciated_2(IEnumerable<Space> spaces, IEnumerable<Panel> panels, out Topology topology, double minArea = Tolerance.MacroDistance, bool updatePanels = true, bool tryCellComplexByCells = true, Log log = null, double silverSpacing = Tolerance.MacroDistance, double tolerance = Tolerance.Distance)
        {
            Log.Add(log, "Method Name: {0}, Tolerance: {1}, Update Panels: {2}", "SAM.Analytical.Topologic.Create.AdjacencyCluster", tolerance, updatePanels);

            topology = null;

            AdjacencyCluster result = new AdjacencyCluster();
            result.AddObjects(spaces);
            result.AddObjects(panels);

            List<Face> faceList = new List<Face>();

            int index = 1;
            foreach (Panel panel in result.GetObjects<Panel>())
            {
                if (panel == null)
                    continue;

                Face face = Convert.ToTopologic(panel);
                if (face == null)
                    continue;

                Dictionary<string, object> dictionary_Temp = new Dictionary<string, object>();
                dictionary_Temp["Panel"] = panel.Guid.ToString();
                face = (Face)face.SetDictionary(dictionary_Temp);

                faceList.Add(face);
                Log.Add(log, "Face {0:D4} added. Panel [{1}]", index, panel.Guid);
                index++;
            }

            if (faceList == null || faceList.Count == 0)
                return null;

            List<CellComplex> cellComplexList = null;
            if (tryCellComplexByCells)
            {
                try
                {
                    Log.Add(log, "Trying to make CellComplex By Cells");
                    Cluster cluster = Cluster.ByTopologies(faceList);
                    Log.Add(log, "Cluster.ByTopologies Done");
                    topology = cluster.SelfMerge();
                    Log.Add(log, "Cluster SelfMerge Done");
                    if (topology.Cells == null || topology.Cells.Count == 0)
                        topology = null;
                    else
                        topology = CellComplex.ByCells(topology.Cells);

                    cellComplexList = new List<CellComplex>() { (CellComplex)topology };
                    Log.Add(log, "CellComplex By Cells Created");
                }
                catch (Exception exception)
                {
                    Log.Add(log, "Cannot create CellComplex By Cells");
                    Log.Add(log, "Exception Message: {0}", exception.Message);
                    cellComplexList = null;
                }
            }

            if (topology == null)
            {
                try
                {
                    Log.Add(log, "Trying to make CellComplex By Faces");
                    topology = CellComplex.ByFaces(faceList, tolerance);
                    cellComplexList = new List<CellComplex>() { (CellComplex)topology };
                    Log.Add(log, "CellComplex By Faces Created");
                }
                catch (Exception exception)
                {
                    Log.Add(log, "Cannot create CellComplex By Faces");
                    Log.Add(log, "Exception Message: {0}", exception.Message);

                    cellComplexList = null;
                }
            }

            if (cellComplexList == null)
                return null;

            //Suprisingly good Michal D. Indea
            if (cellComplexList.Count != 1)
                return null;

            CellComplex cellComplex = cellComplexList[0];

            Log.Add(log, "Single CellComplex created");           
            
            List<Cell> cells = cellComplex.Cells;
            if (cells == null || cells.Count == 0)
                return null;

            Log.Add(log, "Number of Cells: {0}", cells.Count);

            HashSet<Guid> guids_Updated = new HashSet<Guid>();

            Dictionary<Panel, Face3D> dictionary_Panel_Face3D = new Dictionary<Panel, Face3D>();
            result.GetObjects<Panel>().ForEach(x => dictionary_Panel_Face3D[x] = x.GetFace3D());

            List<Space> spaces_Temp = new List<Space>();
            if (spaces != null)
                spaces_Temp.AddRange(spaces);

            Dictionary<Face, Tuple<Face3D, List<Space>>> dictionary = new Dictionary<Face, Tuple<Face3D, List<Space>>>();

            index = 1;
            foreach (Cell cell in cells)
            {
                List<Face> faces = cell?.Faces;
                if (faces == null)
                    continue;

                List<Tuple<Face3D, List<Space>>> tuples = new List<Tuple<Face3D, List<Space>>>();
                foreach (Face face in faces)
                {
                    Tuple<Face3D, List<Space>> tuple = null;
                    if (!dictionary.TryGetValue(face, out tuple))
                    {
                        Face3D face3D = face.ToSAM();
                        if (face3D == null)
                            continue;

                        Face3D face3D_Simplify = face3D.SimplifyByNTS_TopologyPreservingSimplifier(tolerance);
                        if (face3D_Simplify != null)
                            face3D = face3D_Simplify;

                        tuple = new Tuple<Face3D, List<Space>>(face3D, new List<Space>());
                        dictionary[face] = tuple;
                    }
                    tuples.Add(tuple);
                }

                Geometry.Spatial.Shell shell = new Geometry.Spatial.Shell(tuples.ConvertAll(x => x.Item1));

                Log.Add(log, "Simplifying shell");
                shell.Simplify(tolerance);

                Log.Add(log, "Extracting faces from shell");
                List<Face3D> face3Ds = shell?.Face3Ds;
                if (face3Ds == null)
                    continue;

                List<Space> spaces_Shell = spaces_Temp.FindAll(x => shell.On(x.Location, tolerance) || shell.Inside(x.Location, silverSpacing, tolerance));
                if (spaces_Shell.Count != 0)
                {
                    Log.Add(log, "Existing spaces found: {0}", spaces_Shell.Count);
                    spaces_Temp.RemoveAll(x => spaces_Shell.Contains(x));
                }


                if (spaces_Shell.Count == 0)
                {
                    Log.Add(log, "Creating new Space");

                    Point3D location = shell.InternalPoint3D(silverSpacing, tolerance);
                    if (location == null)
                        continue;

                    Space space = new Space("Cell " + index, location);
                    index++;

                    if (!result.AddObject(space))
                        continue;

                    spaces_Shell = new List<Space>() { space };
                }

                if (spaces_Shell == null || spaces_Shell.Count == 0)
                    continue;

                foreach (Tuple<Face3D, List<Space>> tuple in tuples)
                    tuple.Item2.AddRange(spaces_Shell);
            }

            Log.Add(log, "Upadting Panels");
            foreach (Tuple<Face3D, List<Space>> tuple in dictionary.Values)
            {
                Face3D face3D = tuple.Item1;

                if (minArea != 0 && face3D.GetArea() <= minArea)
                    continue;

                Log.Add(log, "Analyzing face and looking for old Panel");
                Panel panel_Old = Query.FindPanel(face3D, dictionary_Panel_Face3D);
                if (panel_Old == null)
                    continue;

                Log.Add(log, "Old Panel found: {0}", panel_Old.Guid);

                Panel panel_New = null;

                if (updatePanels)
                {
                    if (guids_Updated.Contains(panel_Old.Guid))
                    {
                        panel_New = new Panel(Guid.NewGuid(), panel_Old, face3D);
                        Log.Add(log, "Creating new Panel for Old Panel [{0}]. New Panel [{1}]", panel_Old.Guid, panel_New.Guid);
                    }
                    else
                    {
                        panel_New = new Panel(panel_Old.Guid, panel_Old, face3D);
                        guids_Updated.Add(panel_Old.Guid);
                        Log.Add(log, "Updating Panel [{0}] with new geometry", panel_New.Guid);
                    }

                    result.AddObject(panel_New);
                }
                else
                {
                    panel_New = new Panel(panel_Old.Guid, panel_Old, face3D);
                    Log.Add(log, "Creating temporary Panel for Panel [{0}]", panel_New.Guid);
                }

                if (panel_New == null)
                    continue;

                foreach (Space space in tuple.Item2)
                {
                    if (result.AddRelation(space, panel_New))
                        Log.Add(log, "Space [{0}] and Panel [{1}] relation added", space.Guid, panel_New.Guid);
                    else
                        Log.Add(log, "Space [{0}] and Panel [{1}] relation could not be added", space.Guid, panel_New.Guid);
                }

                Log.Add(log, "Adding face finished");
            }

            Log.Add(log, "Sucesfully completed");
            return result;
        }
        
        public static AdjacencyCluster AdjacencyCluster_Depreciated(IEnumerable<Space> spaces, IEnumerable<Panel> panels, out Topology topology, double minArea = Tolerance.MacroDistance, bool updatePanels = true, bool tryCellComplexByCells = true, Log log = null, double tolerance = Tolerance.Distance)
        {
            Log.Add(log, "Method Name: {0}, Tolerance: {1}, Update Panels: {2}", "SAM.Analytical.Topologic.Create.AdjacencyCluster", tolerance, updatePanels);

            topology = null;

            AdjacencyCluster result = new AdjacencyCluster();
            result.AddObjects(spaces);
            result.AddObjects(panels);

            BoundingBox3D boundingBox3D = null;

            List<Face> faceList = new List<Face>();

            int index = 1;
            foreach (Panel panel in result.GetObjects<Panel>())
            {
                if (panel == null)
                    continue;

                Face face = Convert.ToTopologic(panel);
                if (face == null)
                    continue;

                if (boundingBox3D == null)
                {
                    boundingBox3D = panel.GetBoundingBox();
                }
                else
                {
                    BoundingBox3D boundingBox3D_Temp = panel.GetBoundingBox();
                    if (boundingBox3D_Temp != null)
                        boundingBox3D = new BoundingBox3D(new BoundingBox3D[] { boundingBox3D, boundingBox3D_Temp });
                }

                faceList.Add(face);
                Log.Add(log, "Face {0:D4} added. Panel [{1}]", index, panel.Guid);
                index++;
            }

            if (faceList == null || faceList.Count == 0)
                return null;

            List<Topology> topologyList = new List<Topology>();

            index = 1;
            List<Space> spaces_Temp = result.GetObjects<Space>();
            if (spaces_Temp != null && spaces_Temp.Count > 0)
            {
                foreach (Space space in spaces_Temp)
                {
                    if (space == null)
                        continue;

                    Point3D point3D = space.Location;
                    if (point3D.Z - boundingBox3D.Max.Z >= 0)
                        point3D = (Point3D)point3D.GetMoved(new Vector3D(0, 0, (boundingBox3D.Max.Z - boundingBox3D.Min.Z) / 2));

                    if (point3D.Z - boundingBox3D.Min.Z <= 0)
                        point3D = (Point3D)point3D.GetMoved(new Vector3D(0, 0, (boundingBox3D.Max.Z - boundingBox3D.Min.Z) / 2));

                    Vertex vertex = Geometry.Topologic.Convert.ToTopologic(point3D);

                    Dictionary<string, object> dictionary = new Dictionary<string, object>();
                    dictionary["Space"] = space.Guid.ToString();
                    vertex = (Vertex)vertex.SetDictionary(dictionary);
                    topologyList.Add(vertex);

                    Log.Add(log, "Vertex added. Space {0:D4} [{1}] {2}", index, space.Guid, point3D.ToString());
                    index++;
                }
            }

            if (spaces_Temp == null)
                spaces_Temp = new List<Space>();

            List<CellComplex> cellComplexList = null;
            if (tryCellComplexByCells)
            {
                try
                {
                    Log.Add(log, "Trying to make CellComplex By Cells");
                    Cluster cluster = Cluster.ByTopologies(faceList);
                    Log.Add(log, "Cluster.ByTopologies Done");
                    topology = cluster.SelfMerge();
                    Log.Add(log, "Cluster SelfMerge Done");
                    if (topology.Cells == null || topology.Cells.Count == 0)
                        topology = null;
                    else
                        topology = CellComplex.ByCells(topology.Cells);

                    cellComplexList = new List<CellComplex>() { (CellComplex)topology };
                    Log.Add(log, "CellComplex By Cells Created");
                }
                catch (Exception exception)
                {
                    Log.Add(log, "Cannot create CellComplex By Cells");
                    Log.Add(log, "Exception Message: {0}", exception.Message);
                    cellComplexList = null;
                }
            }

            if (topology == null)
            {
                try
                {
                    Log.Add(log, "Trying to make CellComplex By Faces");
                    topology = CellComplex.ByFaces(faceList, tolerance);
                    cellComplexList = new List<CellComplex>() { (CellComplex)topology };
                    Log.Add(log, "CellComplex By Faces Created");
                }
                catch (Exception exception)
                {
                    Log.Add(log, "Cannot create CellComplex By Faces");
                    Log.Add(log, "Exception Message: {0}", exception.Message);

                    cellComplexList = null;
                }
            }

            if (cellComplexList == null)
                return null;

            //Suprisingly good Michal D. Indea
            if (cellComplexList.Count != 1)
                return null;

            CellComplex cellComplex = cellComplexList[0];

            if (spaces_Temp.Count == 0)
            {
                List<Vertex> vertices = cellComplex.Cells.ConvertAll(x => CellUtility.InternalVertex(x, Tolerance.MacroDistance));
                index = 1;

                foreach (Vertex vertex in vertices)
                {
                    Space space = new Space("Cell " + index, Geometry.Topologic.Convert.ToSAM(vertex));
                    index++;

                    if (!result.AddObject(space))
                        continue;

                    spaces_Temp.Add(space);

                    Dictionary<string, object> dictionary = new Dictionary<string, object>();
                    dictionary["Space"] = space.Guid.ToString();
                    Vertex vertex_Temp = (Vertex)vertex.SetDictionary(dictionary);
                    topologyList.Add(vertex_Temp);
                }
            }

            Log.Add(log, "Single CellComplex created");

            if (topologyList != null && topologyList.Count > 0)
            {
                //Issue when adding Contents to cellComplex
                cellComplex = (CellComplex)cellComplex.AddContents(topologyList, 32);

                //List<Cell> cells = cellComplex.Cells;
                //foreach (Vertex vertex in topologyList)
                //{
                //    Cell cell = cells.Find(x => CellUtility.Contains(x, vertex, true, tolerance));
                //    if (cell == null)
                //        continue;

                //    cell.AddContents(new List<Topology>() { vertex }, 32);
                //}
            }

            Log.Add(log, "Dictionaries created");

            HashSet<Guid> guids_Updated = new HashSet<Guid>();

            Dictionary<Panel, Face3D> dictionary_Panel_Face3D = new Dictionary<Panel, Face3D>();
            result.GetObjects<Panel>().ForEach(x => dictionary_Panel_Face3D[x] = x.GetFace3D());

            foreach (Face face_New in cellComplex.Faces)
            {
                if (minArea != 0 && FaceUtility.Area(face_New) <= minArea)
                    continue;

                Log.Add(log, "Converting Topologic face to SAM");
                Face3D face3D = Geometry.Topologic.Convert.ToSAM(face_New);
                if (face3D == null)
                    continue;

                Log.Add(log, "Simplifying face");
                face3D = Geometry.Spatial.Query.SimplifyByNTS_TopologyPreservingSimplifier(face3D, tolerance);

                if (face3D == null || face3D.GetArea() <= minArea)
                    continue;

                Log.Add(log, "Analyzing face and looking for old Panel");
                Panel panel_Old = Query.FindPanel(face3D, dictionary_Panel_Face3D);
                if (panel_Old == null)
                    continue;

                Log.Add(log, "Old Panel found: {0}", panel_Old.Guid);

                Panel panel_New = null;

                if (updatePanels)
                {
                    if (guids_Updated.Contains(panel_Old.Guid))
                    {
                        panel_New = new Panel(Guid.NewGuid(), panel_Old, face3D);
                        Log.Add(log, "Creating new Panel for Old Panel [{0}]. New Panel [{1}]", panel_Old.Guid, panel_New.Guid);
                    }
                    else
                    {
                        panel_New = new Panel(panel_Old.Guid, panel_Old, face3D);
                        guids_Updated.Add(panel_Old.Guid);
                        Log.Add(log, "Updating Panel [{0}] with new geometry", panel_New.Guid);
                    }

                    result.AddObject(panel_New);
                }
                else
                {
                    panel_New = new Panel(panel_Old.Guid, panel_Old, face3D);
                    Log.Add(log, "Creating temporary Panel for Panel [{0}]", panel_New.Guid);
                }

                if (spaces_Temp.Count == 0)
                    continue;

                foreach (Cell cell in face_New.Cells)
                {
                    Log.Add(log, "Analyzing Cell");

                    Space space = null;

                    foreach (Topology topology_Temp in cell.Contents)
                    {
                        Vertex vertex_Space = topology_Temp as Vertex;
                        if (vertex_Space == null)
                            continue;

                        string value_Space = vertex_Space.Dictionary["Space"] as string;
                        Guid guid_Space;
                        if (!Guid.TryParse(value_Space, out guid_Space))
                            continue;

                        space = result.GetObject<Space>(guid_Space);
                        break;
                    }

                    if (space == null)
                        continue;

                    if (result.AddRelation(space, panel_New))
                        Log.Add(log, "Space [{0}] and Panel [{1}] relation added", space.Guid, panel_New.Guid);
                    else
                        Log.Add(log, "Space [{0}] and Panel [{1}] relation could not be added", space.Guid, panel_New.Guid);
                }

                Log.Add(log, "Face finished");
            }

            Log.Add(log, "Sucesfully completed");

            return result;
        }
    }
}