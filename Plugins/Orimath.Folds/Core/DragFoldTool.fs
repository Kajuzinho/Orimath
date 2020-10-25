﻿namespace Orimath.Folds.Core
open System
open System.Reflection
open Orimath.Core
open Orimath.Core.NearlyEquatable
open Orimath.FoldingInstruction
open Orimath.Plugins

type FoldOperation =
    | NoOperation
    | Axiom1 of Point * Point
    | Axiom2 of Point * Point
    | Axiom3 of (LineSegment * Point) * (LineSegment * Point)
    | Axiom4 of LineSegment * Point * isEdge: bool
    | Axiom5 of pass: Point * (LineSegment * Point) * Point
    | Axiom6 of LineSegment * Point * (LineSegment * Point) * Point
    | Axiom7 of pass: LineSegment * LineSegment * Point * isPassEdge: bool
    | AxiomP of LineSegment * Point

type DragFoldTool(workspace: IWorkspace) =
    let paper = workspace.Paper
    let instruction = FoldingInstruction()

    let (|FreePoint|_|) (free: bool) (dt: OperationTarget) =
        match dt.Target with
        | DisplayTarget.Point(point) -> Some(point)
        | DisplayTarget.Layer(_) when free -> Some(dt.Point)
        | _ -> None
    let (|LineOrEdge|_|) (dt: OperationTarget) =
        match dt.Target with
        | DisplayTarget.Line(line) -> Some(line, dt.Point)
        | DisplayTarget.Edge(edge) -> Some(edge.Line, dt.Point)
        | _ -> None
    let (|LineOrEdge2|_|) (dt: OperationTarget) =
        match dt.Target with
        | DisplayTarget.Line(line) -> Some(line, false)
        | DisplayTarget.Edge(edge) -> Some(edge.Line, true)
        | _ -> None

    member private _.GetPass() =
        let passPoint = Array.tryItem 0 paper.SelectedPoints
        let passLine =
            if paper.SelectedEdges.Length > 0
            then Some(paper.SelectedEdges.[0].Line, true)
            elif paper.SelectedLines.Length > 0
            then Some(paper.SelectedLines.[0], false)
            else None
        passPoint, passLine

    member private this.GetOperation(source: OperationTarget, target: OperationTarget, modifier: OperationModifier) =
        let free = modifier.HasFlag(OperationModifier.Alt)
        if modifier.HasFlag(OperationModifier.RightButton) then
            match source, target with
            | FreePoint free (point1), FreePoint free (point2) -> Axiom1(point1, point2)
            | FreePoint free (point), LineOrEdge2(line, isEdge)
            | LineOrEdge2(line, isEdge), FreePoint free (point) -> Axiom4(line, point, isEdge)
            | _ -> NoOperation
        else
            match source, target with
            | FreePoint free (point1), FreePoint free (point2) -> Axiom2(point1, point2)
            | LineOrEdge(line1), LineOrEdge(line2) -> Axiom3(line1, line2)
            | FreePoint free (point), LineOrEdge(line)
            | LineOrEdge(line), FreePoint free (point) ->
                match this.GetPass() with
                | Some(pass), None -> Axiom5(pass, line, point)
                | None, Some(pass, isEdge) -> Axiom7(pass, fst line, point, isEdge)
                | Some(p), Some(l, _) -> Axiom6(l, p, line, point)
                | _ -> AxiomP(fst line, point)
            | _ -> NoOperation
        
    member private _.GetLines(opr) =
        match opr with
        | NoOperation -> []
        | Axiom1(point1, point2) -> Fold.axiom1 point1 point2 |> Option.toList
        | Axiom2(point1, point2) -> Fold.axiom2 point1 point2 |> Option.toList
        | Axiom3((line1, _), (line2, _)) -> Fold.axiom3 line1.Line line2.Line
        | Axiom4(line, point, _) -> [Fold.axiom4 line.Line point]
        | Axiom5(pass, (line, _), point) -> Fold.axiom5 pass line.Line point
        | Axiom6(line1, point1, (line2, _), point2) -> Fold.axiom6 line1.Line point1 line2.Line point2
        | Axiom7(pass, line, point, _) -> Fold.axiom7 pass.Line line.Line point |> Option.toList
        | AxiomP(line, point) -> Fold.axiomP line.Line point |> Option.toList

    member private _.ChooseLine(lines: Line list, opr: FoldOperation) =
        match lines with
        | [] -> None
        | [l] -> Some(l)
        | _ ->
            match opr with
            | Axiom3((_, point1), (_, point2)) ->
                match Fold.axiom1 point1 point2 with
                | None -> Some(lines.[0])
                | Some(opLine) ->
                    match opLine.GetCrossPoint(lines.[0]) with
                    | None -> Some(lines.[0])
                    | Some(cross) ->
                        let x1, x2 = if point1.X <= point2.X then point1.X, point2.X else point2.X, point1.X
                        let y1, y2 = if point1.Y <= point2.Y then point1.Y, point2.Y else point2.Y, point1.Y
                        if x1 <=~ cross.X && cross.X <=~ x2 && y1 <=~ cross.Y && cross.Y <=~ y2
                        then Some(lines.[0])
                        else Some(lines.[1])
            | Axiom5(_, (_, point1), point2)
            | Axiom6(_, _, (_, point1), point2) ->
                Some(lines |> List.minBy(fun line -> point1.GetDistance(line.Reflect(point2))))
            | _ -> Some(lines.[0])

    member private _.SetArrow(source: OperationTarget, target: OperationTarget, chosen: Line, opr: FoldOperation) =
        let getGeneralArrow() =
            match paper.ClipBound(chosen) with
            | None -> Array.Empty()
            | Some(first, last) ->
                let middle = (first + last) / 2.0
                match paper.ClipBound(Fold.axiom4 chosen middle) with
                | None -> Array.Empty()
                | Some(point1, point2) ->
                    let point = if middle.GetDistance(point1) <= middle.GetDistance(point2) then point1 else point2
                    if chosen.Contains(point)
                    then Array.Empty()
                    else [| InstructionArrow.ValleyFold(point, chosen.Reflect(point), InstructionColor.Blue) |]
        let getPerpendicularArrow (line: LineSegment) (chosen: Line) (isEdge: bool) =
            if isEdge then Array.Empty()
            else
                let p1 = chosen.GetSignedDistance(line.Point1)
                let p2 = chosen.GetSignedDistance(line.Point2)
                if sign p1 = sign p2 then Array.Empty()
                else
                    let p = if abs p1 < abs p2 then line.Point1 else line.Point2
                    let reflected = chosen.Reflect(p)
                    if p =~ reflected then Array.Empty()
                    else [| InstructionArrow.ValleyFold(p, chosen.Reflect(p), InstructionColor.Blue) |]
        let arrows, points =
            match opr with
            | NoOperation -> Array.Empty(), Array.Empty()
            | Axiom1(_, _) -> getGeneralArrow(), Array.Empty()
            | Axiom2(point1, point2) -> [| InstructionArrow.ValleyFold(point1, point2, InstructionColor.Blue) |], Array.Empty()
            | Axiom3((_, point), _) -> [| InstructionArrow.ValleyFold(point, chosen.Reflect(point), InstructionColor.Blue) |], Array.Empty()
            | Axiom4(line, _, isEdge) ->
                let perpendicular = getPerpendicularArrow line chosen isEdge
                (if Array.isEmpty perpendicular then getGeneralArrow() else perpendicular), Array.Empty()
            | Axiom5(_, _, point) ->
                let reflected = chosen.Reflect(point)
                [| InstructionArrow.ValleyFold(point, reflected, InstructionColor.Blue) |],
                [| { Point = reflected; Color = InstructionColor.Brown } |]
            | Axiom6(_, point1, _, point2) ->
                let reflected1 = chosen.Reflect(point1)
                let reflected2 = chosen.Reflect(point2)
                [| InstructionArrow.ValleyFold(point1, reflected1, InstructionColor.Blue)
                   InstructionArrow.ValleyFold(point2, reflected2, InstructionColor.Blue) |],
                [| { Point = reflected1; Color = InstructionColor.Brown }
                   { Point = reflected2; Color = InstructionColor.Brown } |]
            | Axiom7(pass, _, point, isEdge) ->
                let reflected = chosen.Reflect(point)
                let arrows =
                    Array.append
                        (getPerpendicularArrow pass chosen isEdge)
                        [| InstructionArrow.ValleyFold(point, reflected, InstructionColor.Blue) |]
                arrows, [| { Point = reflected; Color = InstructionColor.Brown } |]
            | AxiomP(_, point) ->
                match source with
                | LineOrEdge _ -> [| InstructionArrow.ValleyFold(chosen.Reflect(point), point, InstructionColor.Blue) |], Array.Empty()
                | _ -> [| InstructionArrow.ValleyFold(point, chosen.Reflect(point), InstructionColor.Blue) |], Array.Empty()
        instruction.Arrows <- arrows
        instruction.Points <- points

    member _.MakeCrease(line: Line) =
        for layer in paper.Layers do layer.AddLines([line])

    member private _.Split(line: Line, target: LineSegment) =
        match line.GetDistanceSign(target.Point1), line.GetDistanceSign(target.Point2) with
        | 1, 1 | 1, 0 | 0, 1 -> Some(target), None
        | -1, -1 | -1, 0 | 0, -1 -> None, Some(target)
        | 1, -1 ->
            let cross = line.GetCrossPoint(target.Line).Value
            Some(LineSegment.FromPoints(target.Point1, cross).Value),
            Some(LineSegment.FromPoints(cross, target.Point2).Value)
        | -1, 1 ->
            let cross = line.GetCrossPoint(target.Line).Value
            Some(LineSegment.FromPoints(cross, target.Point2).Value),
            Some(LineSegment.FromPoints(target.Point1, cross).Value)
        | _ -> None, None

    member private this.SplitEdges(foldLine: Line, layer: ILayer) =
        let mutable crossed = false
        let mutable isPositive = None
        let positiveEdges = ResizeArray()
        let negativeEdges = ResizeArray()
        let cross (edge: Edge) positiveToNegative =
            let positiveEdge, negativeEdge =
                match layer.ClipBound(foldLine) with
                | Some(a, b) ->
                    if edge.Line.Contains(a) = positiveToNegative
                    then LineSegment.FromPoints(a, b), LineSegment.FromPoints(b, a)
                    else LineSegment.FromPoints(b, a), LineSegment.FromPoints(a, b)
                | None -> None, None
            positiveEdge |> Option.iter(fun e -> positiveEdges.Add(e, true))
            negativeEdge |> Option.iter(fun e -> negativeEdges.Add(e, true))
        for edge in layer.Edges do
            match this.Split(foldLine, edge.Line) with
            | Some(positive), None ->
                if not crossed && isPositive = Some(false) then
                    crossed <- true
                    cross edge false
                positiveEdges.Add(positive, false)
                isPositive <- Some(true)
            | None, Some(negative) ->
                if not crossed && isPositive = Some(true) then
                    crossed <- true
                    cross edge true
                negativeEdges.Add(negative, false)
                isPositive <- Some(false)
            | Some(positive), Some(negative) ->
                if crossed then
                    positiveEdges.Add(positive, false)
                    negativeEdges.Add(negative, false)
                else
                    crossed <- true
                    // 正 → 負に突入
                    if positive.Point2 = negative.Point1 then
                        positiveEdges.Add(positive, false)
                        cross edge true
                        negativeEdges.Add(negative, false)
                        isPositive <- Some(false)
                    // 負 → 正に突入
                    else
                        negativeEdges.Add(negative, false)
                        cross edge false
                        positiveEdges.Add(positive, false)
                        isPositive <- Some(true)
            | _ -> ()
        positiveEdges, negativeEdges

    member private this.SplitLayer(foldLine: Line, isPositiveStatic: bool, layer: ILayer) =
        let turnLayerType isPositive =
            if isPositive = isPositiveStatic then layer.LayerType
            else
                match layer.LayerType with
                | LayerType.BackSide -> LayerType.FrontSide
                | LayerType.FrontSide -> LayerType.BackSide
                | _ -> layer.LayerType
        let positiveEdges, negativeEdges = this.SplitEdges(foldLine, layer)
        let positivePoints = ResizeArray()
        let negativePoints = ResizeArray()
        for point in Seq.append layer.Points (layer.GetCrosses(layer.Clip(foldLine))) do
            match foldLine.GetDistanceSign(point) with
            | 1 -> positivePoints.Add(point)
            | -1 -> negativePoints.Add(point)
            | _ ->
                positivePoints.Add(point)
                negativePoints.Add(point)
        let positiveLines = ResizeArray()
        let negativeLines = ResizeArray()
        for line in layer.Lines do
            let positiveLine, negativeLine = this.Split(foldLine, line)
            positiveLine |> Option.iter positiveLines.Add
            negativeLine |> Option.iter negativeLines.Add
        let createLayer (edges: ResizeArray<LineSegment * bool>) lines points isPositive staticLayer =
            if edges.Count < 3 then None
            else
                if isPositive = isPositiveStatic then
                    workspace.CreateLayer(
                        edges |> Seq.map(fun (e, _) -> Edge.Create(e, None)),
                        lines,
                        points,
                        turnLayerType isPositive)
                else
                    workspace.CreateLayer(
                        edges |> Seq.map(fun (e, _) -> Edge.Create(e.ReflectBy(foldLine), None)),
                        lines |> Seq.map(fun ls -> ls.ReflectBy(foldLine)),
                        points |> Seq.map foldLine.Reflect,
                        turnLayerType isPositive)
                |> Some
        if isPositiveStatic then
            let positiveLayer = createLayer positiveEdges positiveLines positivePoints true None
            let negativeLayer = createLayer negativeEdges negativeLines negativePoints false positiveLayer
            positiveLayer, negativeLayer
        else
            let negativeLayer = createLayer negativeEdges negativeLines negativePoints false None
            let positiveLayer = createLayer positiveEdges positiveLines positivePoints true negativeLayer
            negativeLayer, positiveLayer

    member this.FoldBack(line: Line) =
        let staticLayers = ResizeArray()
        let dynamicLayers = ResizeArray()
        let isPositiveStatic = line.IsPositiveSide({ X = 0.5; Y = 0.5 })
        for layer in paper.Layers do
            let staticLayer, dynamicLayer = this.SplitLayer(line, isPositiveStatic, layer)
            staticLayer |> Option.iter staticLayers.Add
            dynamicLayer |> Option.iter dynamicLayers.Add
        paper.Clear(workspace.CreatePaper(Seq.append staticLayers (Seq.rev dynamicLayers)))

    interface ITool with
        member _.Name = "折り線"
        member _.ShortcutKey = "Ctrl+F"
        member _.Icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Orimath.Folds.Icon.png")
        member _.OnActivated() =
            paper.SelectedLayers <- array.Empty()
            paper.SelectedPoints <- array.Empty()
            paper.SelectedLines <- array.Empty()
            paper.SelectedEdges <- array.Empty()
        member _.OnDeactivated() = ()

    interface IClickTool with
        member this.OnClick(target, modifier) =
            if modifier.HasFlag(OperationModifier.RightButton) then
                match target.Target with
                | DisplayTarget.Line(line) -> this.FoldBack(line.Line)
                | _ -> ()
            else
                let clearOther = not (modifier.HasFlag(OperationModifier.Shift))
                match target.Target with
                | DisplayTarget.Point(point) ->
                    paper.SelectedPoints <-
                        if paper.IsSelected(point) then array.Empty() else [| point |]
                    if clearOther then
                        paper.SelectedLines <- array.Empty()
                        paper.SelectedEdges <- array.Empty()
                | DisplayTarget.Line(line) ->
                    paper.SelectedLines <-
                        if paper.IsSelected(line) then array.Empty() else [| line |]
                    paper.SelectedEdges <- array.Empty()
                    if clearOther then
                        paper.SelectedPoints <- array.Empty()
                | DisplayTarget.Edge(edge) ->
                    paper.SelectedLines <- array.Empty()
                    paper.SelectedEdges <-
                        if paper.IsSelected(edge) then array.Empty() else [| edge |]
                    if clearOther then
                        paper.SelectedPoints <- array.Empty()
                | _ ->
                    paper.SelectedPoints <- array.Empty()
                    paper.SelectedLines <- array.Empty()
                    paper.SelectedEdges <- array.Empty()

    interface IDragTool with
        member _.BeginDrag(source, _) =
            match source with
            | FreePoint true _ | LineOrEdge _ -> true
            | _ -> false

        member this.DragEnter(source, target, modifier) =
            let opr = this.GetOperation(source, target, modifier)
            let lines, chosen =
                match opr with
                | NoOperation ->
                    let lines = this.GetLines(this.GetOperation(source, target, modifier ||| OperationModifier.Alt))
                    lines, None
                | _ ->
                    let lines = this.GetLines(opr)
                    lines, this.ChooseLine(lines, opr)
            let mapping l = {
                  Line = l
                  Color =
                    match chosen with
                    | Some(c) when c =~ l.Line -> InstructionColor.Blue
                    | _ -> InstructionColor.LightGray
                }
            instruction.Lines <- lines
                |> Seq.collect(paper.Clip)
                |> Seq.map mapping
                |> Seq.toArray

            match chosen with
            | Some(c) -> this.SetArrow(source, target, c, opr)
            | None ->
                instruction.Arrows <- Array.Empty()
                instruction.Points <- Array.Empty()

            match target with
            | FreePoint true _ | LineOrEdge _ -> true
            | _ -> false

        member _.DragLeave(_, target, _) =
            instruction.Lines <- Array.Empty()
            instruction.Arrows <- Array.Empty()
            instruction.Points <- Array.Empty()
            match target with
            | FreePoint true _ | LineOrEdge _ -> true
            | _ -> false

        member this.DragOver(source, target, modifier) = (this :> IDragTool).DragEnter(source, target, modifier)

        member this.Drop(source, target, modifier) =
            let opr = this.GetOperation(source, target, modifier)
            match this.ChooseLine(this.GetLines(opr), opr) with
            | Some(line) ->
                use __ = paper.BeginChange()
                if modifier.HasFlag(OperationModifier.Shift)
                then this.FoldBack(line)
                else this.MakeCrease(line)
            | None -> ()
            instruction.Lines <- Array.Empty()
            instruction.Arrows <- Array.Empty()
            instruction.Points <- Array.Empty()

    interface IFoldingInstructionTool with
        member _.FoldingInstruction = instruction
