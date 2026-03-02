package com.unigrid.dataAccess.entities;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;

import java.time.Instant;

@Getter
@Setter
@Entity
@Table(name = "Member_Task", schema = "dbo")
public class MemberTask {
    @EmbeddedId
    private MemberTaskId id;

    @MapsId("memberId")
    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "memberId", nullable = false)
    private Member memberId;

    @MapsId("taskId")
    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "taskId", nullable = false)
    private Task taskId;

    @ColumnDefault("sysdatetime()")
    @Column(name = "assignedAt")
    private Instant assignedAt;


}