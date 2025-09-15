using System;
using System.Numerics;
using System.Collections.Generic;
using AgentsSystem;
using Utils;
using System.Timers;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using M = System.Runtime.CompilerServices.MethodImplAttribute;
using System.Collections;
using System.Text.RegularExpressions;

namespace Agro;

public static class BitArrayExtensions
{
	public static int PopCount(this BitArray bitArray)
	{
		// Copy to int[] (each int holds 32 bits)
		int[] ints = new int[(bitArray.Count + 31) / 32];
		bitArray.CopyTo(ints, 0);

		int count = 0;
		foreach (int value in ints)
			count += BitOperations.PopCount((uint)value);
		return count;
	}

	public static bool Any(this BitArray bitArray)
	{
		// Copy to int[] (each int holds 32 bits)
		int[] ints = new int[(bitArray.Count + 31) / 32];
		bitArray.CopyTo(ints, 0);

		foreach (int value in ints)
			if (value != 0)
				return true;

		return false;
	}

	public static bool IndexOfFirst(this BitArray bitArray, out int result, int skip = 0)
	{
		// Copy to int[] (each int holds 32 bits)
		var ints = new uint[(bitArray.Count + 31) / 32];
		bitArray.CopyTo(ints, 0);

		int blocks = 0;
		for (int i = skip * 8; i < ints.Length; ++i)
		{
			if (ints[i] != 0)
			{
				for (int b = 0; b < 8; ++b)
					if (bitArray.Get(blocks + b))
					{
						result = blocks + b;
						return true;
					}
			}
			blocks += 8;
		}
		result = -1;
		return false;
	}
}

internal readonly record struct Edge
{
	public readonly int A;
	public readonly int B;
	public Edge(int a, int b)
	{
		if (a < b)
		{
			A = a;
			B = b;
		}
		else
		{
			B = a;
			A = b;
		}
	}
}

internal class Component
{
	readonly List<int> Faces = [];
	readonly HashSet<Edge> OuterEdges = [];

	public Component(int face, IList<int> vertices)
	{
		Faces.Add(face);
		for (int i = 1; i < vertices.Count; ++i)
			OuterEdges.Add(new(vertices[i - 1], vertices[i]));
		OuterEdges.Add(new(vertices[^1], vertices[0]));
	}

	public static List<Edge> CreateEdges(List<int> vertices)
	{
		var edges = new List<Edge>(vertices.Count);
		for (int i = 1; i < vertices.Count; ++i)
			edges.Add(new(vertices[i - 1], vertices[i]));
		edges.Add(new(vertices[^1], vertices[0]));
		return edges;
	}

	public bool TryConnect(int face, List<Edge> edges, Component? other = null)
	{
		var innerEdges = new HashSet<Edge>(edges); //connection of the two components will become the inner edge(s)
		innerEdges.IntersectWith((other ?? this).OuterEdges);

		if (innerEdges.Count > 0) //merging
		{
			if (other != null) //this face has been added in the other == null step, now only add other.Faces
			{
				Faces.AddRange(other.Faces);
				OuterEdges.UnionWith(other.OuterEdges);
			}
			else
			{
				Faces.Add(face); //there is no other component to connect with, add only this face
				OuterEdges.UnionWith(edges); //only add the new edges iff other == null, otherwise some inner edges would be falesly readded again
			}
			OuterEdges.ExceptWith(innerEdges);
			return true;
		}
		else
			return false;
	}
}

public class SoilFormationVoronoi : ISoilFormation
{
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
	readonly AgroWorld World;
	///<sumary>
	/// Water amount per cell
	///</sumary>
	readonly float[] Water;
	///<sumary>
	/// Temperature per cell
	///</sumary>
	readonly float[] Temperature;
	///<sumary>
	/// Steam amount per cell
	///</sumary>
	readonly float[] Steam;

	public SoilFormationVoronoi(AgroWorld world, ImportedObjData objData, float scale, string? soilItemRegex)
	{
		World = world;

		var vertices = new Vector3[objData.Vertices.Length];
		for (int i = 0; i < objData.Vertices.Length; ++i)
		{
			var line = Regex.Replace(objData.Vertices[i], @"\s+", " ");
			var vertex = line.Split(' ').Where(x => x?.Length > 0).ToList();
			if (vertex.Count != 3)
				throw new Exception($"Invalid count of vertex coordinates. Expected: 3. Provided: {vertex.Count}.");
			vertices[i] = new Vector3(float.Parse(vertex[0]), float.Parse(vertex[1]), float.Parse(vertex[2])) * scale;
		}
		var regex = soilItemRegex != null ? new Regex(soilItemRegex) : null;

		foreach (var (group, faceLines) in objData.Faces)
			if (regex?.IsMatch(group) ?? true) //for FAV use *Natur_Erde*
			{
				var components = new List<Component>();
				var verts = new List<int>();
				var edges = new List<Edge>();
				var remove = new List<int>();
				for (int i = 0; i < faceLines.Length; ++i)
				{
					var face = faceLines[i].Split(' ');
					verts.Clear();
					verts.AddRange(face.Select(ParseFaceIndex));

					edges.Clear();
					for (int v = 1; v < verts.Count; ++v)
						edges.Add(new(verts[v - 1], verts[v]));
					edges.Add(new(verts[^1], verts[0]));

					remove.Clear();
					var target = -1;
					for (int c = 0; c < components.Count; ++c)
						if (target < 0)
						{
							if (components[c].TryConnect(i, edges))
								target = c;
						}
						else
						{
							if (components[target].TryConnect(i, edges, components[c]))
								remove.Add(c);
						}

					if (target < 0)
						components.Add(new(i, verts));
					else if (remove.Count > 0)
						for (int c = remove.Count - 1; c >= 0; --c)
							components.RemoveAt(remove[c]);
				}

				Console.WriteLine(components.Count);
			}
	}

	public bool HasUndeliveredPost => throw new NotImplementedException();

	public int Count => throw new NotImplementedException();

	public void Census()
	{
		//TODO
	}

	public void DeliverPost(uint timestep)
	{
		//TODO
	}

	public float GetMetricGroundDepth(float x, float z)
	{
		//TODO
		return 0f;
	}

	public float GetTemperature(int index)
	{
		//TODO
		return 20f;
	}

	public float GetWater(int index)
	{
		//TODO
		return 0f;
	}

	public int IntersectPoint(Vector3 center)
	{
		//TODO
		return 0;
	}

	[M(AI)]
	public int ParseFaceIndex(string input)
	{
		input = input.TrimStart();
		var digits = 0;
		for (int i = 0; i < input.Length; ++i)
			if (!char.IsDigit(input[i]))
				break;
			else
				++digits;
		if (digits == 0)
			throw new Exception("Invalid face indexing format.");
		return int.Parse(input[..digits]);
	}

	public void ProcessRequests()
	{
		//TODO
	}

	public void RequestWater(int index, float amount, PlantFormation2 plant)
	{
		//TODO
	}

	public void RequestWater(int index, float amount, PlantSubFormation<UnderGroundAgent> plant, int part)
	{
		//TODO
	}

	public void Tick(uint timestep)
	{
		//TODO
	}
}

