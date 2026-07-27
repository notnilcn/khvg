// From http://www.redblobgames.com/grids/hexagons/implementation.html
// Copyright 2015 Red Blob Games <redblobgames@gmail.com>
// License: Apache v2.0 <http://www.apache.org/licenses/LICENSE-2.0.html>

// This code is used to generate the diagrams on implementation.html

/* global Hex, Layout, Point */

function drawHex(ctx, layout, hex, style={}) {
    var corners = layout.polygonCorners(hex);
    ctx.beginPath();
    ctx.strokeStyle = "black";
    ctx.fillStyle = "white";
    ctx.lineWidth = 1;
    Object.assign(ctx, style);
    ctx.moveTo(corners[5].x, corners[5].y);
    for (var i = 0; i < 6; i++) {
        ctx.lineTo(corners[i].x, corners[i].y);
    }
    ctx.fill();
    ctx.stroke();
}


function colorForHex(hex) {
    // Match the color style used in the main article
    if (hex.q === 0 && hex.r === 0 && hex.s === 0) {
        return "hsl(0, 50%, 0%)";
    } else if (hex.q === 0) {
        return "hsl(90, 70%, 35%)";
    } else if (hex.r === 0) {
        return "hsl(200, 100%, 35%)";
    } else if (hex.s === 0) {
        return "hsl(300, 40%, 50%)";
    } else {
        return "hsl(0, 0%, 50%)";
    }
}


function drawHexLabel(ctx, layout, hex, style={}) {
    const pointSize = Math.round(0.5 * Math.min(Math.abs(layout.size.x), Math.abs(layout.size.y)));
    var center = layout.hexToPixel(hex);
    ctx.fillStyle = colorForHex(hex);
    ctx.font = `${pointSize}px sans-serif`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    Object.assign(ctx, style);
    ctx.fillText(hex.len() === 0? "q,r,s" : (hex.q + "," + hex.r + "," + hex.s), center.x, center.y);
}


function permuteQRS(q, r, s) { return new Hex(q, r, s); }
function permuteSRQ(s, r, q) { return new Hex(q, r, s); }
function permuteSQR(s, q, r) { return new Hex(q, r, s); }
function permuteRQS(r, q, s) { return new Hex(q, r, s); }
function permuteRSQ(r, s, q) { return new Hex(q, r, s); }
function permuteQSR(q, s, r) { return new Hex(q, r, s); }

function shapeParallelogram(q1, r1, q2, r2, constructor) {
    var hexes = [];
    for (var q = q1; q <= q2; q++) {
        for (var r = r1; r <= r2; r++) {
            hexes.push(constructor(q, r, -q-r));
        }
    }
    return hexes;
}


function shapeTriangle1(size) {
    var hexes = [];
    for (var q = 0; q <= size; q++) {
        for (var r = 0; r <= size-q; r++) {
            hexes.push(new Hex(q, r, -q-r));
        }
    }
    return hexes;
}


function shapeTriangle2(size) {
    var hexes = [];
    for (var q = 0; q <= size; q++) {
        for (var r = size-q; r <= size; r++) {
            hexes.push(new Hex(q, r, -q-r));
        }
    }
    return hexes;
}


function shapeHexagon(size) {
    var hexes = [];
    for (var q = -size; q <= size; q++) {
        var r1 = Math.max(-size, -q-size);
        var r2 = Math.min(size, -q+size);
        for (var r = r1; r <= r2; r++) {
            hexes.push(new Hex(q, r, -q-r));
        }
    }
    return hexes;
}


function shapeRectanglePointy(left, top, right, bottom) {
    let hexes = [];
    for (let r = top; r <= bottom; r++) {
        let r_offset = Math.floor(r/2.0); // or r>>1
        for (let q = left - r_offset; q <= right - r_offset; q++) {
            hexes.push(new Hex(q, r, -q-r));
        }
    }
    return hexes;
}

function shapeRectangleFlat(left, top, right, bottom) {
    let hexes = [];
    for (let q = left; q <= right; q++) {
        let q_offset = Math.floor(q/2.0); // or q>>1
        for (let r = top - q_offset; r <= bottom - q_offset; r++) {
            hexes.push(new Hex(q, r, -q-r));
        }
    }
    return hexes;
}

function shapeRectangleArbitrary(w, h, constructor) {
    var hexes = [];
    var i1 = -Math.floor(w/2), i2 = i1 + w;
    var j1 = -Math.floor(h/2), j2 = j1 + h;
    for (var j = j1; j < j2; j++) {
        var jOffset = -Math.floor(j/2);
        for (var i = i1 + jOffset; i < i2 + jOffset; i++) {
            hexes.push(constructor(i, j, -i-j));
        }
    }
    return hexes;
}


function drawGrid({id, labels, layout, hexes, axes, xMarkers, yMarkers}) {
    const margin = axes ? {left: 30, right: 2, top: 2, bottom: 20} : {left: 0, right: 0, top: 0, bottom: 0};
    labels = labels ?? false;
    hexes = hexes ?? shapeRectangleArbitrary(15, 15, permuteQRS);
    
    const canvas = /** @type{HTMLCanvasElement} */(document.getElementById(id));
    if (!canvas) { console.warn(`Could not find canvas id=${id}`); return; }
    const ctx = canvas.getContext('2d');

    const width = canvas.width;
    const height = canvas.height;
    const innerWidth = width - margin.left - margin.right;
    const innerHeight = height - margin.bottom - margin.top;
    if (window.devicePixelRatio && window.devicePixelRatio != 1) {
        canvas.width = width * window.devicePixelRatio;
        canvas.height = height * window.devicePixelRatio;
        ctx.scale(window.devicePixelRatio, window.devicePixelRatio);
    }

    ctx.clearRect(0, 0, width, height);

    // Draw the axes
    if (axes) {
        const gridSpacing = 10;
        ctx.save();
        ctx.font = "10px sans-serif";
        ctx.fillStyle = "hsl(180 30% 30%)"
        ctx.translate(margin.left + innerWidth/2, margin.top + innerHeight/2);

        ctx.textAlign = "right";
        const firstRow = Math.ceil(-innerHeight/2 / gridSpacing),
              lastRow = Math.floor(innerHeight/2 / gridSpacing);
        for (let r = firstRow; r <= lastRow; r++) {
            const y = gridSpacing * r;
            const major = r % 5 === 0;
            ctx.strokeStyle = major? "hsl(180 30% 50%)" : "hsl(180 30% 80%)";
            ctx.beginPath();
            ctx.moveTo(-innerWidth/2 - (major ? 7 : 5), y);
            ctx.lineTo(innerWidth/2, y);
            ctx.stroke();
            if (major) ctx.fillText(r * gridSpacing, -innerWidth/2- 7, y + 3);
        }

        ctx.textAlign = "center";
        const firstColumn = Math.ceil(-innerWidth/2 / gridSpacing),
              lastColumn = Math.floor(innerWidth/2 / gridSpacing);
        for (let q = firstColumn; q <= lastColumn; q++) {
            const x = gridSpacing * q;
            const major = q % 5 === 0;
            ctx.strokeStyle = major? "hsl(180 30% 50%)" : "hsl(180 30% 80%)";
            ctx.beginPath();
            ctx.moveTo(x, -innerHeight/2);
            ctx.lineTo(x, innerHeight/2 + (major ? 7 : 5));
            ctx.stroke();
            if (major) ctx.fillText(q * gridSpacing, x, innerHeight/2 + 15);
        }
        ctx.restore();
    }

    // Draw the hexagons
    ctx.save();
    const clipArea = new Path2D();
    clipArea.rect(margin.left, margin.top, innerWidth, innerHeight);
    ctx.clip(clipArea);
    ctx.translate(margin.left + innerWidth/2, margin.top + innerHeight/2);
    hexes.forEach(function(hex) {
        drawHex(ctx, layout, hex);
        if (labels) drawHexLabel(ctx, layout, hex);
    });
    ctx.restore();

    // Draw any horizontal/vertical lines
    ctx.save();
    ctx.beginPath();
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = "hsl(180 30% 30% / 0.5)";
    ctx.setLineDash([3, 5]);
    for (let x of xMarkers ?? []) {
        ctx.moveTo(margin.left + innerWidth/2 + x, margin.top);
        ctx.lineTo(margin.left + innerWidth/2 + x, margin.top + innerHeight);
    }
    for (let y of yMarkers ?? []) {
        ctx.moveTo(margin.left, margin.top + innerHeight/2 + y);
        ctx.lineTo(margin.left + innerWidth, margin.top + innerHeight/2 + y);
    }
    ctx.stroke();
    ctx.restore();
}


drawGrid({id: "layout-test-orientation-pointy", labels: true,
          layout: new Layout(Layout.pointy, new Point(25, 25), new Point(0, 0))});
drawGrid({id: "layout-test-orientation-flat", labels: true,
          layout: new Layout(Layout.flat, new Point(25, 25), new Point(0, 0))});

drawGrid({id: "layout-test-size-1", bg: "hsl(60, 10%, 85%)", axes: true,
          layout: new Layout(Layout.pointy, new Point(10, 10), new Point(0, 0))});
drawGrid({id: "layout-test-size-2", bg: "hsl(60, 10%, 90%)", axes: true,
          layout: new Layout(Layout.pointy, new Point(25, 25), new Point(0, 0))});
drawGrid({id: "layout-test-size-3", bg: "hsl(60, 10%, 85%)", axes: true,
          layout: new Layout(Layout.pointy, new Point(50, 50), new Point(0, 0))});

drawGrid({id: "layout-test-size-tall", bg: "hsl(60, 10%, 90%)", axes: true, labels: true,
          layout: new Layout(Layout.flat, new Point(100/2, 100/Math.sqrt(3)), new Point(100/2, 100/2)),
          xMarkers: [0, 100], yMarkers: [0, 100]});
drawGrid({id: "layout-test-size-wide", bg: "hsl(60, 10%, 85%)", axes: true, labels: true,
          layout: new Layout(Layout.pointy, new Point(100/Math.sqrt(3), 100/2), new Point(100/2, 100/2)),
          xMarkers: [0, 100], yMarkers: [0, 100]});

drawGrid({id: "layout-test-y-down", labels: true, axes: true,
          layout: new Layout(Layout.pointy, new Point(25, 25), new Point(0, 0)),
          yMarkers: [50]});
drawGrid({id: "layout-test-y-up", labels: true, axes: true,
          layout: new Layout(Layout.pointy, new Point(25, -25), new Point(0, 0)),
          yMarkers: [-50]});

drawGrid({id: "layout-test-origin-flat-centered", labels: true, axes: true,
          layout: new Layout(Layout.flat, new Point(50, 50), new Point(0, 0)),
          xMarkers: [0], yMarkers: [0]});
drawGrid({id: "layout-test-origin-flat-topleft", labels: true, axes: true,
          layout: new Layout(Layout.flat, new Point(50, 50), new Point(50, 50*Math.sqrt(3)/2)),
          xMarkers: [0, 100], yMarkers: [0, 50*Math.sqrt(3)]});
drawGrid({id: "layout-test-origin-pointy-centered", labels: true, axes: true,
          layout: new Layout(Layout.pointy, new Point(50, 50), new Point(0, 0)),
          xMarkers: [0], yMarkers: [0]});
drawGrid({id: "layout-test-origin-pointy-topleft", labels: true, axes: true,
          layout: new Layout(Layout.pointy, new Point(50, 50), new Point(50*Math.sqrt(3)/2, 50)),
          xMarkers: [0, 50*Math.sqrt(3)], yMarkers: [0, 100]});

drawGrid({id: "shape-pointy-parallelogram-qr", labels: true,
          layout: new Layout(Layout.pointy, new Point(15, 15), new Point(0, 0)),
          hexes: shapeParallelogram(-2, -2, 2, 2, permuteQRS)});
drawGrid({id: "shape-pointy-parallelogram-sq", labels: true,
          layout: new Layout(Layout.pointy, new Point(13, 13), new Point(0, 0)),
          hexes: shapeParallelogram(-2, -2, 2, 2, permuteSQR)});
drawGrid({id: "shape-pointy-parallelogram-rs", labels: true,
          layout: new Layout(Layout.pointy, new Point(15, 15), new Point(0, 0)),
          hexes: shapeParallelogram(-2, -2, 2, 2, permuteRSQ)});

drawGrid({id: "shape-flat-parallelogram-qr", labels: true,
          layout: new Layout(Layout.flat, new Point(15, 15), new Point(0, 0)),
          hexes: shapeParallelogram(-2, -2, 2, 2, permuteQRS)});
drawGrid({id: "shape-flat-parallelogram-sq", labels: true,
          layout: new Layout(Layout.flat, new Point(15, 15), new Point(0, 0)),
          hexes: shapeParallelogram(-2, -2, 2, 2, permuteSQR)});
drawGrid({id: "shape-flat-parallelogram-rs", labels: true,
          layout: new Layout(Layout.flat, new Point(13, 13), new Point(0, 0)),
          hexes: shapeParallelogram(-2, -2, 2, 2, permuteRSQ)});

drawGrid({id: "shape-pointy-triangle-1", labels: true,
          layout: new Layout(Layout.pointy, new Point(15, 15), new Point(-70, -60)),
          hexes: shapeTriangle1(5)});
drawGrid({id: "shape-pointy-triangle-2", labels: true,
          layout: new Layout(Layout.pointy, new Point(15, 15), new Point(-130, -60)),
          hexes: shapeTriangle2(5)});

drawGrid({id: "shape-flat-triangle-1", labels: true,
          layout: new Layout(Layout.flat, new Point(15, 15), new Point(-60, -70)),
          hexes: shapeTriangle1(5)});
drawGrid({id: "shape-flat-triangle-2", labels: true,
          layout: new Layout(Layout.flat, new Point(15, 15), new Point(-60, -130)),
          hexes: shapeTriangle2(5)});

drawGrid({id: "shape-pointy-hexagon", labels: true,
          layout: new Layout(Layout.pointy, new Point(15, 15), new Point(0, 0)),
          hexes: shapeHexagon(3)});
drawGrid({id: "shape-flat-hexagon", labels: true,
          layout: new Layout(Layout.flat, new Point(15, 15), new Point(0, 0)),
          hexes: shapeHexagon(3)});

drawGrid({id: "shape-pointy-rectangle-topleft", labels: true,
          layout: new Layout(Layout.pointy, new Point(20, 20), new Point(-110, -60)),
          hexes: shapeRectanglePointy(0, 0, +6, +4)});
drawGrid({id: "shape-pointy-rectangle-centered", labels: true,
          layout: new Layout(Layout.pointy, new Point(20, 20), new Point(0, 0)),
          hexes: shapeRectanglePointy(-3, -2, +3, +2)});
drawGrid({id: "shape-flat-rectangle-topleft", labels: true,
          layout: new Layout(Layout.flat, new Point(20, 20), new Point(-90, -75)),
          hexes: shapeRectangleFlat(0, 0, +6, +4)});
drawGrid({id: "shape-flat-rectangle-centered", labels: true,
          layout: new Layout(Layout.flat, new Point(20, 20), new Point(0, -10)),
          hexes: shapeRectangleFlat(-3, -2, +3, +2)});
