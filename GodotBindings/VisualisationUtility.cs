using Godot;
using System;

enum marker_direction{
    bidirectional,
    positive,
    negative
}
// positive => axis direction, negative => opposite

enum marker_axis{
    X,
    Y,
    Z
}

class MarkerData{
    public marker_direction MarkerDireciton; //This is handy for corner-cases
    public marker_axis MarkerAxis;
    public Tuple<int,int,int> MeshInstanceIndex;
    public Tuple<int,int> PositiveFlowIndex; //To access flow infromation
    public Tuple<int,int> NegativeFlowIndex;

    public bool was_negative = false;

    public MarkerData(marker_direction direction, Tuple<int,int,int> mesh_instance_index, Tuple<int,int> positive_flow_index, Tuple<int,int> negative_flow_index, marker_axis MarkAxis){
        MarkerDireciton = direction;
        MarkerAxis = MarkAxis;
        MeshInstanceIndex = mesh_instance_index;
        PositiveFlowIndex = positive_flow_index;
        NegativeFlowIndex = negative_flow_index;
    }
}