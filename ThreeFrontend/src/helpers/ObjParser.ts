
export interface IObjImport
{
    vertices: string[];
    normals: string[];
    faces: any;
}

export async function Parse(f: File) : Promise<IObjImport> {
    const scale = 0.001;
    const vertices: string[] = [];
    const vertexMap: number[] = [];
    const normals: string[] = [];
    const normalMap: number[] = [];
    const faces = new Map<string, string[]>();

    const decoder = new TextDecoder("utf-8");
    const reader = f.stream().getReader();
    let buffer = "";

    let currentFace = "";
    let { value: chunk, done} = await reader.read();
    while (!done) {
        buffer += decoder.decode(chunk, { stream: true });
        let lines = buffer.split("\n");

        // Keep the last partial line in the buffer
        buffer = lines.pop();

        processLines(lines);

        ({ value: chunk, done } = await reader.read());
    }

    // Flush any remaining text
    buffer += decoder.decode();
    if (buffer)
        processLines(buffer.split('\n'));

    function processLines(lines: string[]) {
        for (let line of lines) {
            line = line.replaceAll('\r', '');
            if (line.startsWith('v ')) {
                const text = line.substring(2);
                if (!vertices.includes(text))
                    vertices.push(text);

                vertexMap.push(vertexMap.length);
            }
            else if (line.startsWith('vn ')) {
                const text = line.substring(2);
                if (!normals.includes(text))
                    normals.push(text);
                normalMap.push(normalMap.length);
            }
            else if (line.startsWith('g ')) {
                currentFace = line.substring(2);
                faces.set(currentFace, []);
            }
            else if (line.startsWith('f ')) {
                const faceVertices = line.substring(1).trimStart().split(" ").filter(x => x?.length > 0);
                let vertexString: string[] = [];
                for (const vertex of faceVertices) {
                    const components = vertex.split('/');
                    switch (components.length) {
                        default: vertexString.push(components[0]); break;
                        case 3: vertexString.push(`${vertexMap[parseInt(components[0])]}:${normalMap[parseInt(components[2])]}`); break;
                    }
                }
                faces.get(currentFace).push(vertexString.join(' '));
            }
        }
    }
debugger;
    return {
        vertices: vertices,
        normals: normals,
        faces: Object.fromEntries(faces)
    }
}