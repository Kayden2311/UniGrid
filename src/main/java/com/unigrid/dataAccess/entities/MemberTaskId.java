package com.unigrid.dataAccess.entities;

import jakarta.persistence.Column;
import jakarta.persistence.Embeddable;
import lombok.EqualsAndHashCode;
import lombok.Getter;
import lombok.Setter;

import java.io.Serializable;
import java.util.UUID;

@Getter
@Setter
@EqualsAndHashCode
@Embeddable
public class MemberTaskId implements Serializable {
    private static final long serialVersionUID = -7464110591061394025L;
    @Column(name = "memberId", nullable = false)
    private UUID memberId;

    @Column(name = "taskId", nullable = false)
    private UUID taskId;


}