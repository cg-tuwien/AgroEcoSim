using Godot;
using System;

public static class GeoBuilder{

    public static Mesh UnitCube(){ //I've created this method because the default one isn't unit cube and it's problematic to get unit cube "Mesh" from it.
        var temp = new ArrayMesh();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        st.AddVertex(new Vector3(-0.5f, -0.5f, 0.5f));
        st.AddVertex(new Vector3(0.5f, -0.5f, 0.5f));
        st.AddVertex(new Vector3(0.5f, -0.5f, -0.5f));
        st.AddVertex(new Vector3(-0.5f, -0.5f, -0.5f));
        st.AddVertex(new Vector3(-0.5f, 0.5f, 0.5f));
        st.AddVertex(new Vector3(0.5f, 0.5f, 0.5f));
        st.AddVertex(new Vector3(0.5f, 0.5f, -0.5f));
        st.AddVertex(new Vector3(-0.5f, 0.5f, -0.5f));

        st.AddIndex(5); st.AddIndex(1); st.AddIndex(0);
        st.AddIndex(4); st.AddIndex(5); st.AddIndex(0);

        st.AddIndex(0); st.AddIndex(1); st.AddIndex(2);
        st.AddIndex(0); st.AddIndex(2); st.AddIndex(3);

        st.AddIndex(5); st.AddIndex(6); st.AddIndex(2);
        st.AddIndex(5); st.AddIndex(2); st.AddIndex(1);

        st.AddIndex(6); st.AddIndex(7); st.AddIndex(3);
        st.AddIndex(6); st.AddIndex(3); st.AddIndex(2);

        st.AddIndex(7); st.AddIndex(4); st.AddIndex(3);
        st.AddIndex(4); st.AddIndex(0); st.AddIndex(3);

        st.AddIndex(4); st.AddIndex(6); st.AddIndex(5);
        st.AddIndex(7); st.AddIndex(6); st.AddIndex(4);

        st.GenerateNormals();
        st.Commit(temp);


        return temp;
    }

    public static Mesh UnitPyramid4(){
        var temp = new ArrayMesh();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        st.AddVertex(new Vector3(-0.5f, -0.5f, 0.5f));
        st.AddVertex(new Vector3(0.5f, -0.5f, 0.5f));
        st.AddVertex(new Vector3(0.5f, -0.5f, -0.5f));
        st.AddVertex(new Vector3(-0.5f, -0.5f, -0.5f));
        st.AddVertex(new Vector3(0f, 0.5f, 0f));

        st.AddIndex(0); st.AddIndex(1); st.AddIndex(2);
        st.AddIndex(2); st.AddIndex(3); st.AddIndex(0);
        st.AddIndex(0); st.AddIndex(4); st.AddIndex(1);
        st.AddIndex(1); st.AddIndex(4); st.AddIndex(2);
        st.AddIndex(2); st.AddIndex(4); st.AddIndex(3);
        st.AddIndex(3); st.AddIndex(4); st.AddIndex(0);

        st.GenerateNormals();
        st.Commit(temp);

        return temp;
    }
}