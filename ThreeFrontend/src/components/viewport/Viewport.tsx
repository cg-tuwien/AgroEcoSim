import { Attributes, Component, ComponentChild, ComponentChildren, Ref, createRef, h } from "preact";
import { findDOMNode } from "preact/compat";
import appstate from "../../appstate";
// import ThreeScene from "./ThreeScene";


// export interface SceneInteractionModel {
//     pickedShape: number | null;
//     pickedAsSelection: boolean;

//     // displayBase: DisplayMode;
//     // displayElements: Map<string, DisplayMode>;
//     // displayShapes: Map<number, DisplayMode>;

//     //cullingBox: number[];
// }

// interface IProps {
//     pickAction: (shapeId: number) => void;
//     unpickAction: () => void;
//     selectAction: (shapeId: number) => void;
//     unselectAction: () => void;
//     toggleSelectAction: (shapeId: number) => void;
//     interaction: SceneInteractionModel;
// }

// interface IState  {
//     width: number;
//     height: number;
//     parent?: HTMLElement;
//     initialized: boolean;
// };

// export class Viewport extends Component<IProps>
// {
//     state: IState = {
//         width: 0,
//         height: 0,
//         initialized: false,
//     };

//     isRendering: boolean = false;

//     sceneRef = createRef<ThreeScene>();

//     render() {
//         return <ThreeScene
//             width={this.state.width}
//             height={this.state.height}
//             ref={this.sceneRef}
//             onHover={id => id >= 0 ? this.props.pickAction(id) : this.props.unpickAction()}
//             onDblClick={id => this.props.toggleSelectAction(id)}
//             hasSelection={() => this.props.interaction?.pickedAsSelection ?? false}
//         />;
//     }

//     componentDidMount() {
//         const parent = findDOMNode(this)?.parentElement;
//         parent && this.setState(s => ({ parent: parent }));

//         window.addEventListener("resize", this.onResizeWindow);
//         // window.addEventListener("mousemove", this.onMouseMove);
//         this.onResizeWindow();
//     }

//     componentWillUnmount() {
//         window.removeEventListener("resize", this.onResizeWindow);
//     }

//     forceResize = (rect: DOMRect) => this.setState({width: rect.width, height: rect.height});

//     private onResizeWindow = () => {
//         const rect = this.state.parent?.getBoundingClientRect();
//         rect && this.setState({width: rect.width, height: rect.height });
//     }
// }

export class Viewport extends Component
{
    render() {
        return <p>{appstate.scene}</p>;
    }
}