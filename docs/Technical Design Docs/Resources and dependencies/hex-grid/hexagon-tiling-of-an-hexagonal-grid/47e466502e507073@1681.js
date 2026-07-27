// https://observablehq.com/@sanderevers/hexagon-tiling-of-an-hexagonal-grid@1681
import define1 from "./a46446d8701dfc65@35.js";
import define2 from "./8d271c22db968ab0@160.js";

function _1(md){return(
md`
# Hexagon tiling of an hexagonal grid

In this notebook I will show a method for filling a hexagonal grid with larger hexagons, like so:`
)}
/*
```js */
function _2(small_to_big,my,d3,hexgrid,conf1)
{ 
  function smallhex_color(xyz) {
    const [i,j,k] = small_to_big(xyz);
    const nr = my.mod(i-j,3);
    return d3.color(d3.interpolateRainbow((nr % 5)/5)).brighter(1.3);
  }

  const {svg,hexagon} = hexgrid({...conf1, unit_space:25, height:250});
  hexagon(0.9).attr("fill",smallhex_color)
  return svg.node();
}
/*
```
*/

function _3(md,tex){return(
md`This doesn't look particularly difficult to do, at least procedurally. The centers of the big hexagons make up an evenly spaced grid, and once you've calculated those centers you can start assigning all small hexagons with distance-to-center ${tex`\leq r`} to that specific big hexagon; one possible way to do this would be by spiraling outwards from the center.

However, it would be nicer to have a formula which, given the coordinates of a small hex, tells you directly which big hex it belongs to. With this formula we could just color the grid in a top-to-bottom, left-to-right way, for example. More importantly, it would be suitable for visualization libraries like [D3.js](https://d3js.org/), in which you apply the same function to all data points. So, this formula is what this notebook is about.`
)}

function _4(md){return(
md`## A grid of small hexes`
)}

function _5(md,tex){return(
md`The small hexes can be given integer coordinates. I'll use a system of [cube coordinates](https://www.redblobgames.com/grids/hexagons/#coordinates-cube) ${tex`x,y,z`} for this, with ${tex`x+y+z=0`}. They work like this:`
)}

function _6(hexgrid,conf1,form1,d3,my)
{ 
  const {svg,center_text,corner_text,hexagon} = hexgrid({...conf1,height:250,unit_space:45});
  function select_col(cc) {
    const hc = cc[form1.coord];
    return d3.color(d3.interpolatePuBuGn(0.2+my.mod(hc,8)/24));
  }
  hexagon().attr("fill",select_col).attr("stroke","grey");
  corner_text(5,0.6)
    .attr("fill",form1.coord==0?"black":"grey")
    .style("font-weight",form1.coord==0?"bold":"normal")
    .text(cc => cc[0]);
  corner_text(1,0.5)
    .attr("fill",form1.coord==1?"black":"grey")
    .style("font-weight",form1.coord==1?"bold":"normal")
    .text(cc => cc[1]);
  corner_text(3,0.6)
    .attr("fill",form1.coord==2?"black":"grey")
    .style("font-weight",form1.coord==2?"bold":"normal")
    .text(cc => cc[2]);
  return svg.node();
}


function _form1(form,html,tex){return(
form(html`<form>&nbsp;🌈 &nbsp;click to highlight cube coordinate 
  <label style="margin-right:0.5em;"><input type=radio name='coord' value="0" checked> ${tex`x`}</label>
  <label style="margin-right:0.5em;"><input type=radio name='coord' value="1"> ${tex`y`}</label>
  <label style="margin-right:0.5em;"><input type=radio name='coord' value="2"> ${tex`z`}</label>
</form>`)
)}

function _8(md,tex){return(
md`I'll assume we already have code that can draw a small hex with some given ${tex`x,y,z`} coordinates at the right place and in a color of our choice; see [Amit Patel's excellent guide](https://www.redblobgames.com/grids/hexagons) for how to do this.`
)}

function _9(md){return(
md`## From small to big`
)}

function _10(md,tex){return(
md`In the picture at the top, you can see that the big hexes also form a grid, in much the same way as the small hexes. This second grid is wider spaced and rotated by approximately 30º with respect to the first grid. To identify a big hex, I'll refer to it by its cube coordinates ${tex`i,j,k`} in this second grid.

The function that will do the hard work is the one that maps the ${tex`x,y,z`} coordinates of a small hex to the ${tex`i,j,k`} coordinates of the big hex it belongs to. Once we have those, coloring the small hex is relatively easy. Without further ado, this function reads as follows:`
)}

function _small_to_big(my,shift,area){return(
function small_to_big(xyz) {
   const [x,y,z] = xyz;
   const xh = my.div(y+shift*x, area),
         yh = my.div(z+shift*y, area),
         zh = my.div(x+shift*z, area);
   const i = my.div(1+xh-yh, 3),
         j = my.div(1+yh-zh, 3),
         k = my.div(1+zh-xh, 3);
   return [i,j,k,xh,yh,zh];
}
)}

function _12(md){return(
md`Two minor remarks about this definition:
- \`my.div\` is floored integer division.
- Apart from \`i,j,k\`, the function also returns some intermediate results \`xh,yh,zh\`. This is not necessary for its primary job but we'll use it later on to visualize its inner working.
`
)}

function _13(md,tex){return(
md`But this is basically it: some linear transformation and integer division. Notice the identifiers \`area\` and \`shift\` ⏤ these are two constants related to the size of the big hexes. The *area* is the number of small hexes they consist of, and \`shift\` is a helper constant we'll discuss later. Both can be defined in terms of the *radius*: the [distance](https://www.redblobgames.com/grids/hexagons/#distances) between the center and the outer ring.  For radius *r*, they are given by:

\`area\` = ${tex`3r^2+3r+1\\`}
\`shift\` = ${tex`3r+2`}

In the examples shown here we have`
)}

function _r(){return(
2
)}

function _area(r){return(
3*(r*r+r)+1
)}

function _shift(r){return(
3*r+2
)}

function _17(md,r){return(
md`By the way, if you're not familiar with Observable, now is a good time to point out that all the cells in this notebook are *editable*: by clicking in their left margin they fold out to show their source code. (Try it on the cell with \`r = ${r}\` above. After changing the value, press Shift-Enter and the whole notebook will update automatically!)`
)}

function _18(md){return(
md`## Coloring the big hexes`
)}

function _19(md,tex){return(
md`Using the ${tex`i,j,k`} coordinates, we can paint all the small hexes belonging to one big hex the same color. It would be nice if no two adjacent big hexes have the same color. One way to do this with just three colors is by choosing color ${tex`(i-j) \operatorname{mod} 3`}:`
)}

function _choose_color_number(small_to_big,my){return(
function choose_color_number(xyz) {
  const [x,y,z]= xyz;
  const [i,j,k,xh,yh,zh] = small_to_big(xyz);
  return my.mod(i-j,3);
  //return x;
  //return i;
  //return yh;
}
)}

function _21(md){return(
md`The numbers this function returns are shown below, all given a different color. Feel free to play around with \`choose_color_number\` and observe the results.`
)}

function _22(choose_color_number,d3,hexgrid,conf1)
{ 
  function smallhex_color(cc) {
    const nr = choose_color_number(cc);
    return d3.color(d3.interpolateRainbow((nr % 7)/7)).brighter(1.1);
  }
  
  const {svg,center_text,corner_text,hexagon,hexaside} = hexgrid({...conf1,height:400,unit_space:25});
  hexagon(0.9).attr("fill",smallhex_color)
  center_text().text(choose_color_number);
  // hexaside(0).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside_black(0));
  // hexaside(2).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside_black(2));
  // hexaside(4).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside_black(4));

  return svg.node();
}


function _23(md){return(
md`## Some explanation`
)}

function _24(md){return(
md`The \`small_to_big\` function I presented here is inspired by a pixel-to-hex function that Amit Patel refers to as the "branchless method" on [this page](https://www.redblobgames.com/grids/hexagons/more-pixel-to-hex.html). The first part calculates the values \`xh\`, \`yh\` and \`zh\` for a small hex. Each of these can be visualised as a sequence of strips that cut each big hex in half:`
)}

function _25(hexgrid,conf1,width,small_to_big,form2,my,shift,area,bigside,r,d3,html)
{ 
  const {svg,center_text,corner_text,hexagon,hexaside,gridpoint,at,cc2xy} = hexgrid({...conf1,height:350,unit_space:22, width:width/2-20});
  function select_col(cc) {    
    const [i,j,k,xr,yr,zr] = small_to_big(cc);
    const sel_coord = [xr,yr,zr][form2.coord];
    return my.mod(sel_coord,2)==0 ? "goldenrod":"grey";
  }
  const is_center = cc => my.mod(cc[2]+shift*cc[1],area)==0;
  svg.append('svg:defs').append('svg:marker')
    .attr('id', 'end-arrow')
    .attr('viewBox', '0 -5 10 10')
    .attr('refX', 6)
    .attr('markerWidth', 4)
    .attr('markerHeight', 4)
    .attr('orient', 'auto')
    .append('svg:path')
    .attr('d', 'M0,-5L10,0L0,5')
    .attr('fill', 'black');
  
  hexagon().attr("fill",select_col).attr("stroke","white").attr("opacity",0.4);
  center_text()
    .text(cc => is_center(cc)?cc:null);
  hexaside(0).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside(0,"white"));
  hexaside(2).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside(2,"white"));
  hexaside(4).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside(4,"white"));

  const b1 = my.rotate(my.augment([r,r+1]),3-form2.coord);
  const b2 = my.rotate(my.augment([-1,shift]),3-form2.coord);
  svg.append("path")
    .attr("d",d3.line()([my.scale(b1,0.2),my.scale(b1,0.8)].map(cc2xy)))
    .attr("stroke-width",3)
    .attr("stroke","black")
    .style('marker-end', "url(#end-arrow)");
  svg.append("path")
    .attr("d",d3.line()([my.scale(b2,0.1),my.scale(b2,0.9)].map(cc2xy)))
    .attr("stroke-width",3)
    .attr("stroke","black")
    .style('marker-end', "url(#end-arrow)");
  
  const b1_label = my.scale(b1,0.7);
  b1_label[form2.coord] = b1_label[form2.coord]-1.2;
  const b2_label = my.scale(b2,0.7);
  b2_label[form2.coord] = b2_label[form2.coord]-1.2;
  at(b1_label).append("text").text("b1");
  at(b2_label).append("text").text("b2");
  
  // corner_text(5,0.6)
  //   .style("font-weight",show_coord[0]?"bold":"normal")
  //   .attr("fill",show_coord[0]?"black":"grey")
  //   .text(cc => is_center(cc)?cc[0]:null);
  // corner_text(1,0.4)
  //   .style("font-weight",show_coord[1]?"bold":"normal")
  //   .attr("fill",show_coord[1]?"black":"grey")
  //   .text(cc => is_center(cc)?cc[1]:null);
  // corner_text(3,0.6)
  //   .style("font-weight",show_coord[2]?"bold":"normal")
  //   .attr("fill",show_coord[2]?"black":"grey")
  //   .text(cc => is_center(cc)?cc[2]:null);
  

  //center_text().text('x').style('font-weight','bold');
  const stroke_start=my.scale(my.rotate(my.augment([1,1]),form2.coord),1/3);
  const stroke_end=my.scale(my.rotate(my.augment([-1,-1]),form2.coord),1/3);
  const text_even=my.scale(my.rotate(my.augment([-1,1]),form2.coord),1/4);
  const text_odd=my.scale(my.rotate(my.augment([1,-1]),form2.coord),1/4);
  
  const hl = hexgrid({...conf1,height:350,unit_space:22*(2*r+1)*0.5*Math.sqrt(3),width:width/2-20,swapxy:true});
  hl.hexagon().attr("fill","white").attr("stroke","black");
  hl.gridpoint.append("path")
    .attr("d",d3.line()([stroke_start,stroke_end].map(hl.cc2xy)))
    .attr("stroke-dasharray","10 7")
    .attr("stroke","black")
  hl.gp_at(text_even).append("text")
    .style("dominant-baseline","middle")
    .attr("text-anchor","middle")
    .text(cc=>cc[my.mod(-form2.coord-1,3)]?null:2*cc[my.mod(-form2.coord+1,3)]);
  hl.gp_at(text_odd).append("text")
    .style("dominant-baseline","middle")
    .attr("text-anchor","middle")
    .text(cc=>cc[my.mod(-form2.coord-1,3)]?null:2*cc[my.mod(-form2.coord+1,3)]-1);
    
  
  return html`${hl.svg.node()} ${svg.node()}`;
}


function _form2(form,html){return(
form(html`<form>&nbsp;🌈 &nbsp;click to show 
  <label style="margin-right:0.5em;"><input type=radio name='coord' value="0" checked> <tt>xh</tt></label>
  <label style="margin-right:0.5em;"><input type=radio name='coord' value="1"> <tt>yh</tt></label>
  <label style="margin-right:0.5em;"><input type=radio name='coord' value="2"> <tt>zh</tt></label>
</form>`)
)}

function _27(md,f2,tex){return(
md`On the left side, the \`${f2.ph}\` strips with their different values are shown schematically. In this world, the ${tex`x,y,z`} coordinates are real numbers, as is the radius ${tex`r`}; the integer value \`${f2.ph}\` is then simply calculated as ${tex`\lfloor ${f2.p}/r \rfloor`}.`
)}

function _28(md,f2){return(
md`The \`${f2.ph}\` strips on the right side work in mostly the same way. However, the big hexes are not as nicely aligned here. They are shifted by one small hex every time, causing the strips to be jagged. It turns out that we can compensate for this by (pardon my linear algebra) switching to the coordinate system with the following basis:`
)}

function _29(tex){return(
tex.block`b_1=\begin{bmatrix} r \\ r+1 \end{bmatrix}
\hspace{5em}
b_2=\begin{bmatrix} -1 \\ 3r+2 \end{bmatrix}`
)}

function _30(md,tex,f2){return(
md`which is also shown on the right. (These vectors themselves are in ${tex`${f2.p},${f2.s}`} coordinates.) If we define \`${f2.ph}\` as the ${tex`b_1`} coordinate, we get`
)}

function _31(tex,f2){return(
tex.block`\begin{bmatrix} ${f2.ph} \\ \cdot \end{bmatrix} =
\begin{bmatrix} b_1 & b_2 \end{bmatrix}^{-1}
\begin{bmatrix} ${f2.p} \\ ${f2.s} \end{bmatrix} =
\frac{1}{3r^2+3r+1}\begin{bmatrix} 3r+2 & 1 \\ -r-1 & r\end{bmatrix}
\begin{bmatrix} ${f2.p} \\ ${f2.s} \end{bmatrix}`
)}

function _32(md,f2){return(
md`or in other words: \`${f2.ph} = div(${f2.s}+shift*${f2.p},area)\`.`
)}

function _33(md){return(
md`The second part of the \`small_to_big\` function,`
)}

function _34(md,f2){return(
md`> \`${f2.res} = div(1+${f2.ph}-${f2.sh}, 3)\``
)}

function _35(md,f2,tex){return(
md`weaves the \`${f2.ph}\` and \`${f2.sh}\` strips into the 120º zig-zags that make up the ${tex`${f2.res}`} coordinate:`
)}

function _36(hexgrid,conf1,small_to_big,form2,my,shift,area,bigside)
{ 
  const {svg,center_text,corner_text,hexagon,hexaside,gridpoint,at} = hexgrid({...conf1,height:250,unit_space:22});
  function choose_color_number(xyz) {
    const ijk = small_to_big(xyz);
    return ijk[form2.coord];
  }
  function select_col(xyz) {
    return my.mod(choose_color_number(xyz),2)==0 ? "goldenrod":"grey";
  }
  const is_center = cc => my.mod(cc[2]+shift*cc[1],area)==0;
  
  hexagon().attr("fill",select_col).attr("stroke","white").attr("opacity",0.4);
  center_text().text(cc => is_center(cc)?choose_color_number(cc):null);
  hexaside(0).attr("stroke-width",2).attr("stroke-linecap","round").attr("stroke",bigside(0,"white"));
  hexaside(2).attr("stroke-width",2).attr("stroke-linecap","round").attr("stroke",bigside(2,"white"));
  hexaside(4).attr("stroke-width",2).attr("stroke-linecap","round").attr("stroke",bigside(4,"white"));

  return svg.node()
}


function _37(md){return(
md`... but how this part works so nicely I cannot explain.`
)}

function _38(md){return(
md`## See also`
)}

function _39(md){return(
md`👉 [Hexmod representation](https://observablehq.com/@sanderevers/hexmod-representation)`
)}

function _appendix(md){return(
md`## Appendix`
)}

function _conf1(width)
{return {width, height:600, unit_space:40}}


function _bigside(small_to_big){return(
function bigside(nr,color) {
  const neighbors = [[1,0,-1],[0,1,-1],[-1,1,0],[-1,0,1],[0,-1,1],[1,-1,0]];
  const [xn,yn,zn] = neighbors[nr];
  return function(xyz) {
    const [x,y,z] = xyz;
    const [i,j,k] = small_to_big(xyz);
    const [i2,j2,k2] = small_to_big([x+xn,y+yn,z+zn]);
    const different = i-j-(i2-j2);
    return different?color:null;
  }
}
)}

function _f2(form2,my)
{
  const p = ['x','y','z'][form2.coord];
  const s = ['x','y','z'][my.mod(form2.coord+1,3)];
  const t = ['x','y','z'][my.mod(form2.coord+2,3)];
  const res = ['i','j','k'][form2.coord];
  const ph = p+'h';
  const sh = s+'h';
  const th = t+'h';
  const pv = cc=>cc[form2.coord];
  const sv = cc=>cc[my.mod(form2.coord+1,3)];
  const tv = cc=>cc[my.mod(form2.coord+2,3)];
  return {p,s,t,ph,sh,th,pv,sv,tv,res};
}


export default function define(runtime, observer) {
  const main = runtime.module();
  main.variable(observer()).define(["md"], _1);
  main.variable(observer()).define(["small_to_big","my","d3","hexgrid","conf1"], _2);
  main.variable(observer()).define(["md","tex"], _3);
  main.variable(observer()).define(["md"], _4);
  main.variable(observer()).define(["md","tex"], _5);
  main.variable(observer()).define(["hexgrid","conf1","form1","d3","my"], _6);
  main.variable(observer("viewof form1")).define("viewof form1", ["form","html","tex"], _form1);
  main.variable(observer("form1")).define("form1", ["Generators", "viewof form1"], (G, _) => G.input(_));
  main.variable(observer()).define(["md","tex"], _8);
  main.variable(observer()).define(["md"], _9);
  main.variable(observer()).define(["md","tex"], _10);
  main.variable(observer("small_to_big")).define("small_to_big", ["my","shift","area"], _small_to_big);
  main.variable(observer()).define(["md"], _12);
  main.variable(observer()).define(["md","tex"], _13);
  main.variable(observer("r")).define("r", _r);
  main.variable(observer("area")).define("area", ["r"], _area);
  main.variable(observer("shift")).define("shift", ["r"], _shift);
  main.variable(observer()).define(["md","r"], _17);
  main.variable(observer()).define(["md"], _18);
  main.variable(observer()).define(["md","tex"], _19);
  main.variable(observer("choose_color_number")).define("choose_color_number", ["small_to_big","my"], _choose_color_number);
  main.variable(observer()).define(["md"], _21);
  main.variable(observer()).define(["choose_color_number","d3","hexgrid","conf1"], _22);
  main.variable(observer()).define(["md"], _23);
  main.variable(observer()).define(["md"], _24);
  main.variable(observer()).define(["hexgrid","conf1","width","small_to_big","form2","my","shift","area","bigside","r","d3","html"], _25);
  main.variable(observer("viewof form2")).define("viewof form2", ["form","html"], _form2);
  main.variable(observer("form2")).define("form2", ["Generators", "viewof form2"], (G, _) => G.input(_));
  main.variable(observer()).define(["md","f2","tex"], _27);
  main.variable(observer()).define(["md","f2"], _28);
  main.variable(observer()).define(["tex"], _29);
  main.variable(observer()).define(["md","tex","f2"], _30);
  main.variable(observer()).define(["tex","f2"], _31);
  main.variable(observer()).define(["md","f2"], _32);
  main.variable(observer()).define(["md"], _33);
  main.variable(observer()).define(["md","f2"], _34);
  main.variable(observer()).define(["md","f2","tex"], _35);
  main.variable(observer()).define(["hexgrid","conf1","small_to_big","form2","my","shift","area","bigside"], _36);
  main.variable(observer()).define(["md"], _37);
  main.variable(observer()).define(["md"], _38);
  main.variable(observer()).define(["md"], _39);
  main.variable(observer("appendix")).define("appendix", ["md"], _appendix);
  const child1 = runtime.module(define1);
  main.import("hexgrid", child1);
  main.import("my", child1);
  main.import("d3", child1);
  main.variable(observer("conf1")).define("conf1", ["width"], _conf1);
  main.variable(observer("bigside")).define("bigside", ["small_to_big"], _bigside);
  main.variable(observer("f2")).define("f2", ["form2","my"], _f2);
  const child2 = runtime.module(define2);
  main.import("form", child2);
  return main;
}
