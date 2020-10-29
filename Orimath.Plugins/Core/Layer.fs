﻿namespace Orimath.Core
open System.Collections.Generic
open System.Runtime.CompilerServices
open NearlyEquatable

type LayerType =
    | BackSide = 0
    | FrontSide = 1

type Edge (line: LineSegment, inner: bool) =
    member _.Line = line
    member _.Inner = inner

    override _.ToString() = line.ToString()

type ILayer =
    abstract member Edges : IReadOnlyList<Edge>
    abstract member Lines : IReadOnlyList<LineSegment>
    abstract member Points : IReadOnlyList<Point>
    abstract member LayerType : LayerType
    abstract member OriginalEdges : IReadOnlyList<Edge>
    abstract member Matrix : Matrix

[<Extension>]
type LayerExtensions =
    static member ContainsCore(edges, point: Point) =
        let rec recSelf acm (edges: Edge list) =
            match edges with
            | head :: tail ->
                let p1 = head.Line.Point1
                let p2 = head.Line.Point2
                if head.Line.Contains(point) then
                    true
                else
                    if (p1.Y <= point.Y && point.Y < p2.Y || p2.Y <= point.Y && point.Y < p1.Y) &&
                        point.X < head.Line.Line.GetX(point.Y) then
                        recSelf (acm + 1) tail
                    else
                        recSelf acm tail
            | [] -> acm % 2 = 1
        recSelf 0 edges

    /// このレイヤーの領域に指定した点が含まれているか判断します。
    [<Extension>]
    static member Contains(layer: ILayer, point) = LayerExtensions.ContainsCore(asList layer.Edges, point)

    /// このレイヤーの領域に指定した線分が完全に含まれているか判断します。
    [<Extension>]
    static member Contains(layer: ILayer, line: LineSegment) = layer.Contains(line.Point1) && layer.Contains(line.Point2)

    /// このレイヤーの領域に全ての点が含まれているか判断します。
    [<Extension>]
    static member ContainsAll(layer: ILayer, points: seq<Point>) = points |> Seq.forall(layer.Contains)

    /// このレイヤーの領域に全ての線分が完全に含まれているか判断します。
    [<Extension>]
    static member ContainsAll(layer: ILayer, lines: seq<LineSegment>) = lines |> Seq.forall(layer.Contains)

    /// このレイヤーに、指定した点と同じ点が存在するか判断します。
    [<Extension>]
    static member HasPoint(layer: ILayer, point) = layer.Points |> Seq.exists((=~) point)

    /// このレイヤーに、指定した線分と同じ線分が存在するか判断します。
    [<Extension>]
    static member HasLine(layer: ILayer, line: LineSegment) =
        layer.Edges |> Seq.exists(fun e -> e.Line.Line =~ line.Line) ||
        layer.Lines |> Seq.exists(fun l -> l.Contains(line))

    /// このレイヤーに、指定した直線と同じ線分が存在するか判断します。
    [<Extension>]
    static member HasLine(layer: ILayer, line: Line) =
        layer.Clip(line)
        |> Seq.forall(layer.HasLine : LineSegment -> bool)

    /// このレイヤーの範囲内に収まるように、指定された直線をカットします。
    [<Extension>]
    static member Clip(layer: ILayer, line: Line) =
        let points = ResizeArray()
        for edge in layer.Edges do
            match edge.Line.Line.GetCrossPoint(line) with
            | Some(p) when edge.Line.Contains(p) && not (points |> Seq.exists((=~) p))
                -> points.Add(p)
            | _ -> ()
        points
        |> Seq.sortBy(fun p -> if line.YFactor = 0.0 then p.Y else p.X)
        |> Seq.pairwise
        |> Seq.filter(fun (p1, p2) -> layer.Contains((p1 + p2) / 2.0))
        |> Seq.choose(LineSegment.FromPoints)
        
    /// このレイヤーの範囲内に収まるように指定された直線をカットし、その両端の位置を返します。
    [<Extension>]
    static member ClipBound(layer: ILayer, line: Line) =
        let segments = layer.Clip(line) |> Seq.toArray
        if segments.Length = 0 then
            None
        else
            Some(segments.[0].Point1, segments.[segments.Length - 1].Point2)

    /// このレイヤーの範囲内に収まるように、指定された線分をカットします。
    [<Extension>]
    static member Clip(layer: ILayer, line: LineSegment) =
        let points = ResizeArray()
        points.Add(line.Point1)
        points.Add(line.Point2)
        for edge in layer.Edges do
            match edge.Line.GetCrossPoint(line) with
            | Some(p) when not (points |> Seq.exists((=~) p)) -> points.Add(p)
            | _ -> ()
        points
        |> Seq.sortBy(fun p -> if line.Line.YFactor = 0.0 then p.Y else p.X)
        |> Seq.pairwise
        |> Seq.filter(fun (p1, p2) -> layer.Contains((p1 + p2) / 2.0))
        |> Seq.choose(LineSegment.FromPoints)
        
    /// このレイヤーの範囲内に収まるように指定された線分をカットし、その両端の位置を返します。
    [<Extension>]
    static member ClipBound(layer: ILayer, line: LineSegment) =
        let segments = layer.Clip(line) |> Seq.toArray
        if segments.Length = 0 then
            None
        else
            Some(segments.[0].Point1, segments.[segments.Length - 1].Point2)

    static member private TryAddPoint(layer: ILayer, points: Point list, addingPoint: Point option) =
        match addingPoint with
        | Some(p) when (points |> List.forall((<>~) p)) && not (layer.HasPoint(p)) ->
            p :: points
        | _ -> points

    static member private AppendCross(layer: ILayer, line: LineSegment, points: Point list) =
        let mutable points = points
        for edge in layer.Edges do
            points <- LayerExtensions.TryAddPoint(layer, points, edge.Line.GetCrossPoint(line))
        for layerLine in layer.Lines do
            points <- LayerExtensions.TryAddPoint(layer, points, layerLine.GetCrossPoint(line))
        points
            
    /// このレイヤー内の全ての折線と、指定した線分との交点を取得します。
    [<Extension>]
    static member GetCrosses(layer: ILayer, line: LineSegment) = LayerExtensions.AppendCross(layer, line, [line.Point1; line.Point2])
            
    /// このレイヤー内の全ての折線と、指定した全ての線分との交点を取得します。
    [<Extension>]
    static member GetCrosses(layer: ILayer, lines: seq<LineSegment>) =
        let rec recSelf (lines: LineSegment list) points =
            let mutable points = points
            match lines with
            | line :: tail ->
                if (points |> List.forall((<>~) line.Point1)) && not (layer.HasPoint(line.Point1)) then points <- line.Point1 :: points
                if (points |> List.forall((<>~) line.Point2)) && not (layer.HasPoint(line.Point2)) then points <- line.Point2 :: points
                points <- LayerExtensions.AppendCross(layer, line, points)
                for tailLine in tail do points <- LayerExtensions.TryAddPoint(layer, points, tailLine.GetCrossPoint(line))
                recSelf tail points
            | _ -> points
        recSelf (asList lines) []

    /// このレイヤーの OriginalEdges を辺として持ち、点・折線を持たないレイヤーを取得します。
    [<Extension>]
    static member GetOriginal(layer: ILayer) =
        { new ILayer with
            member _.Edges = layer.OriginalEdges
            member _.Lines = upcast []
            member _.Points = upcast []
            member _.LayerType = layer.LayerType
            member _.OriginalEdges = layer.OriginalEdges
            member _.Matrix = Matrix.Identity
        }

    /// LayerType を裏表逆転させます。
    [<Extension>]
    static member TurnOver(layerType: LayerType) =
        match layerType with
        | LayerType.FrontSide -> LayerType.BackSide
        | LayerType.BackSide -> LayerType.FrontSide
        | _ -> layerType
