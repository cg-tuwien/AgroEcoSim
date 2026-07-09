import appstate from "../appstate";

export interface IObjImport
{
    vertices: string[];
    //normals: string[];
    faces: any;
    materials: any;
}

export async function Parse(f: File) : Promise<string> {
    const decoder = new TextDecoder("utf-8");
    const reader = f.stream().getReader();
    let buffer = "";
    let { value: chunk, done} = await reader.read();
    var remaining = f.size;
    while (!done) {
        buffer += decoder.decode(chunk, { stream: true });
        remaining -= chunk.byteLength;
        appstate.modelParsingProgress.value = remaining;
        ({ value: chunk, done } = await reader.read());
    }
    buffer += decoder.decode();
    appstate.modelParsingProgress.value = 0;
    return buffer;
}

export async function ParseOld(f: File) : Promise<IObjImport> {
    const scale = 0.001;
    const vertices: string[] = [];
    const verticesSet = new Map<string, number>();
    const vertexMap: number[] = [];
    const normals: string[] = [];
    const normalMap: number[] = [];
    const faces = new Map<string, string[]>();
    const materials = new Map<string, string>();
    let currentObjectFaces : string[] = undefined;

    const decoder = new TextDecoder("utf-8");
    const reader = f.stream().getReader();
    let buffer = "";

    let currentObjectName = "";
    let { value: chunk, done} = await reader.read();
    var remaining = f.size;
    while (!done) {
        buffer += decoder.decode(chunk, { stream: true });
        let lines = buffer.split("\n");

        // Keep the last partial line in the buffer
        buffer = lines.pop();

        processLines(lines);

        remaining -= chunk.byteLength;
        appstate.modelParsingProgress.value = remaining;
        ({ value: chunk, done } = await reader.read());
    }

    // Flush any remaining text
    buffer += decoder.decode();
    if (buffer)
        processLines(buffer.split('\n'));

    appstate.modelParsingProgress.value = 0;

    function processLines(lines: string[]) {
        for (let line of lines) {
            line = line.replaceAll('\r', '');
            if (line.startsWith('v ')) {
                const text = line.substring(2);
                let mapped = verticesSet.get(text);
                if (mapped >= 0)
                    vertexMap.push(mapped);
                else
                {
                    mapped = verticesSet.size;
                    verticesSet.set(text, mapped);
                    vertexMap.push(mapped);
                    vertices.push(text);
                }
            }
            // else if (line.startsWith('vn ')) {
            //     const text = line.substring(2);
            //     if (!normals.includes(text))
            //         normals.push(text);
            //     normalMap.push(normalMap.length);
            // }
            //else if (line.startsWith('g ')) { }
            else if (line.startsWith('o ')) {
                currentObjectName = line.substring(2);
                currentObjectFaces = []
                faces.set(currentObjectName, currentObjectFaces);
                //Naming conventions:
                //P1_Y00_S01 = P is the higher order structure (facade region), Y is the vertical row, S is the soil (box volume) within the given tray
                //P1_Y00_T01 = P is the higher order structure (facade region), Y is the vertical row, T is the tray geometry
                //P1_Y00_T01_D01 is a delimeter within the given tray
                //P1_Y00_T01_CAP_END is the end cap of the given tray
                //P1_Y00_T01_CAP_START is the start cap of the given tray
            }
            else if (line.startsWith('usemtl ')) {
                materials.set(currentObjectName, line.substring(7));
            }
            else if (line.startsWith('f ')) {
                const faceVertices = line.substring(2).trimStart().split(" ").filter(x => x?.length > 0);
                let vertexString: string[] = [];
                for (const vertex of faceVertices) {
                    //const components = vertex.split('/');
                    // switch (components.length) {
                    //     default: vertexString.push(components[0]); break;
                    //     case 3: vertexString.push(`${vertexMap[parseInt(components[0])]}:${normalMap[parseInt(components[2])]}`); break;
                    // }
                    //vertexString.push(components[0]);

                    const slashIndex = vertex.indexOf('/');
                    vertexString.push(slashIndex >= 0 ? vertex.substring(0, slashIndex) : vertex);
                }
                currentObjectFaces.push(vertexString.join(' '));
            }
        }
    }
    debugger;
    const result = {
        vertices: vertices,
        //normals: normals,
        faces: Object.fromEntries(Array.from(faces, ([key, group]) => [key, group.map(x => x.split(' ').map(v => vertexMap[parseInt(v) - 1]).join(' '))])),
        materials: Object.fromEntries(materials)
    }
    appstate.modelParsingProgress.value = 0;
    return result;
}