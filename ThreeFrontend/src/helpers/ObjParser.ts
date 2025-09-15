import appstate from "../appstate";

export interface IObjImport
{
    vertices: string[];
    //normals: string[];
    faces: any;
}

export async function Parse(f: File) : Promise<IObjImport> {
    const scale = 0.001;
    const vertices: string[] = [];
    const verticesSet = new Set<string>();
    const vertexMap: number[] = [];
    const normals: string[] = [];
    const normalMap: number[] = [];
    const faces = new Map<string, string[]>();
    let currentGroupFaces : string[] = undefined;

    const decoder = new TextDecoder("utf-8");
    const reader = f.stream().getReader();
    let buffer = "";

    let currentGroupName = "";
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
                if (!verticesSet.has(text))
                {
                    verticesSet.add(text);
                    vertices.push(text);
                }

                vertexMap.push(vertexMap.length);
            }
            // else if (line.startsWith('vn ')) {
            //     const text = line.substring(2);
            //     if (!normals.includes(text))
            //         normals.push(text);
            //     normalMap.push(normalMap.length);
            // }
            else if (line.startsWith('g ')) {
                currentGroupName = line.substring(2);
                currentGroupFaces = []
                faces.set(currentGroupName, currentGroupFaces);
            }
            else if (line.startsWith('f ')) {
                const faceVertices = line.substring(1).trimStart().split(" ").filter(x => x?.length > 0);
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
                currentGroupFaces.push(vertexString.join(' '));
            }
        }

        appstate.modelParsingProgress.value = 0;
    }
    return {
        vertices: vertices,
        //normals: normals,
        faces: Object.fromEntries(faces)
    }
}