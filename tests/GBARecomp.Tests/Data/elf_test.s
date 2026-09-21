    .syntax unified
    .text
    .arm
    .global ARMStart
    .type ARMStart, %function
ARMStart:
    mov r0, #1
    bx lr
    .size ARMStart, .-ARMStart
    .thumb
    .global ThumbMain
    .type ThumbMain, %function
    .thumb_func
ThumbMain:
    push {lr}
    bl ThumbHelper
local_label:
    pop {pc}
    .size ThumbMain, .-ThumbMain
    .type ThumbHelper, %function
    .thumb_func
ThumbHelper:
    movs r0, #0
    bx lr
    .size ThumbHelper, .-ThumbHelper
    .section .rodata
    .global SomeData
SomeData:
    .word 0x12345678
    .global SomeConstant
    .set SomeConstant, 4
