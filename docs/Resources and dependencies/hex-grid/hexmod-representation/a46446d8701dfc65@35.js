// https://observablehq.com/@sanderevers/hex-grid@35
function _1(md){return(
md`# Hex grid`
)}

function _2(md){return(
md`A library for drawing a grid of hexagons. In development. Usage: see [\`@sanderevers/hexagon-tiling-of-an-hexagonal-grid\`](https://observablehq.com/@sanderevers/hexagon-tiling-of-an-hexagonal-grid). Comments welcome.`
)}

function _3(hexgrid,width)
{
  const {svg,center_text,hexagon} = hexgrid({width,height:200,unit_space:40})
  hexagon(0.9).attr('fill','lightgray');
  center_text().text(xy => `${xy[0]},${xy[1]}`);
  return svg.node()
}


function _hexgrid(d3,my){return(
function hexgrid(config) {
  const {width,height,unit_space,swapxy} = config;
  //const tf = `matrix(${unit_space},0,${unit_space*0.5},${-unit_space*0.5*Math.sqrt(3)},0,0)`;
  //const itf = `matrix(${1/unit_space},0,${1/(unit_space*Math.sqrt(3))},${-1/(unit_space*0.5*Math.sqrt(3))},0,0)`;

  const corners = [[1,1], [-1,2], [-2,1], [-1,-1], [1,-2], [2,-1]];
  //const corners = [[2,1], [1,2], [-1,1], [-2,-1], [-1,-2], [1,-1]];

  //const nx = Math.ceil(width/unit_space);
  //const ny = Math.ceil(height/(unit_space*0.5*Math.sqrt(3)));
  
  function cc2xy(cc) {
    const x = unit_space * (cc[0]+0.5*cc[1]);
    const y = unit_space * (-0.5*Math.sqrt(3)*cc[1]);
    return swapxy?[-y,-x]:[x,y];
  }
  
  function translate_cc(cc) {
    const [x,y] = cc2xy(cc);
    return `translate(${x},${y})`;
  }
  
  const visible_cc = [];
  for (let x=Math.floor(-width/(2*unit_space)); x<=Math.ceil(width/(2*unit_space)); x++)
    for (let y=Math.floor(-height/(unit_space*Math.sqrt(3))); y<=Math.ceil(height/(unit_space*Math.sqrt(3))); y++)
        visible_cc.push([x-Math.floor(y/2),y,-y-x+Math.floor(y/2)]);
  
  // function text_trans(cornr) {
  //   const xy = corners[cornr];
  //   return `translate(${xy[0]*0.2},${xy[1]*0.2})`;
  // }
  
  const svg = d3.create("svg")
    .attr("width", width)
    .attr("height", height)
    .attr("viewBox",`${-width / 2} ${-height / 2} ${width} ${height}`);
  
  const gridpoint = svg.append("g")
    //.attr("transform",tf)
    .selectAll("g")
    .data(visible_cc)
    .join("g")
    .attr("transform", translate_cc);

  const gridpoint_top = svg.append("g")
    //.attr("transform",tf)
    .selectAll("g")
    .data(visible_cc)
    .join("g")
    .attr("transform", translate_cc);

  function corner_text(cornr,scale) {
    const scale_c = x => x*(scale || 0.5)/3;
    const ccc = corners[cornr];
   
    return gridpoint_top
      .append("g")
      .attr("transform",translate_cc(ccc.map(scale_c)))
      .append("text")
      //.attr("transform",itf)
      .style("dominant-baseline","middle")
      .attr("text-anchor","middle")
      .style("font-family","sans-serif")
      .style("font-size","10pt")
  }
  
  function center_text() {
    return gridpoint
      .append("text")
 //     .attr("transform",itf)
      .attr("text-anchor","middle")
      .style("dominant-baseline","middle")
      .style("font-family","sans-serif")
      .style("font-size","10pt");
  }
  
  function hexagon(scale) {
    const scale_c = x => x*(scale || 1)/3;
    const translate_corner = cc => cc2xy(cc.map(scale_c));
    const hexaline = d3.line()(corners.map(translate_corner))+"Z";
    return gridpoint
      .append("path")
      .attr("d",hexaline);
  }
  
  function hexaside(nr,scale) {
    const scale_c = x => x*(scale || 1)/3;
    const translate_corner = cc => cc2xy(cc.map(scale_c));
    const sideline = d3.line()([corners[my.mod(nr-1,6)],corners[nr]].map(translate_corner));
    return gridpoint_top
      .append("path")
      .attr("d",sideline);    
  }
  
  function at(cc) {
    return svg.append("g").attr("transform",translate_cc(cc));
  }
  function gp_at(cc) {
    return gridpoint.append("g").attr("transform",translate_cc(cc));
  }
  
  return {svg,gridpoint,corner_text,center_text,hexagon,hexaside,at,gp_at,cc2xy};
}
)}

function _my()
{ 
  const mod = (x,y) => ((x % y) + y) % y;
  const div = (x,y) => Math.floor(x/y); 
  function rotate(arr,n) {
    const ret = arr.slice(n);
    ret.push(...arr.slice(0,n));
    return ret;
  }
  function augment(xy) {
    const [x,y] = xy;
    return [x,y,-x-y];
  }
  const scale = (v,a) => v.map(x => x*a);
  const add = (v1,v2) => v1.map((x,i) => x+v2[i]);
  return {mod,div,rotate,augment,scale,add};
}


function _d3(require){return(
require('d3@5')
)}

export default function define(runtime, observer) {
  const main = runtime.module();
  main.variable(observer()).define(["md"], _1);
  main.variable(observer()).define(["md"], _2);
  main.variable(observer()).define(["hexgrid","width"], _3);
  main.variable(observer("hexgrid")).define("hexgrid", ["d3","my"], _hexgrid);
  main.variable(observer("my")).define("my", _my);
  main.variable(observer("d3")).define("d3", ["require"], _d3);
  return main;
}
